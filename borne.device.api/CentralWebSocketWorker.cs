using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using borne.business.Services;
using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;

namespace borne.device.api;

public sealed class CentralWebSocketWorker(
    BorneRuntimeOptions options,
    CentralLinkStatus linkStatus,
    IBorneLocalStore store,
    BorneOperationService operations,
    TimeProvider timeProvider,
    ILogger<CentralWebSocketWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan retryDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(stoppingToken);
                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                linkStatus.SetConnected(false);
                logger.LogWarning("Central link ended ({FailureType}); queued Borne data remains on local storage.",
                    exception.GetType().Name);
                await Task.Delay(WithJitter(retryDelay), stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.SetRequestHeader("Authorization", $"Bearer {options.CentralApiToken}");
        await socket.ConnectAsync(BuildWebSocketUri(), cancellationToken);
        linkStatus.SetConnected(true);
        logger.LogInformation("Central link connected for Borne {BorneId}.", options.BorneId);

        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sendLock = new SemaphoreSlim(1, 1);
        var acknowledgements = new ConcurrentDictionary<Guid, TaskCompletionSource<bool>>();
        Task receiveTask = ReceiveLoopAsync(socket, sendLock, acknowledgements, connectionCts.Token);
        Task heartbeatTask = HeartbeatLoopAsync(socket, sendLock, connectionCts.Token);
        Task outboxTask = OutboxLoopAsync(socket, sendLock, acknowledgements, connectionCts.Token);
        try
        {
            Task completed = await Task.WhenAny(receiveTask, heartbeatTask, outboxTask);
            await completed;
        }
        finally
        {
            await connectionCts.CancelAsync();
            socket.Abort();
            try { await Task.WhenAll(receiveTask, heartbeatTask, outboxTask); }
            catch (Exception) { }
            sendLock.Dispose();
            linkStatus.SetConnected(false);
        }
    }

    private async Task HeartbeatLoopAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            await SendEnvelopeAsync(socket, sendLock, "heartbeat", Guid.NewGuid(), new
            {
                borneId = options.BorneId,
                sentAtUtc = now
            }, cancellationToken);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private async Task OutboxLoopAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        ConcurrentDictionary<Guid, TaskCompletionSource<bool>> acknowledgements,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<PendingBorneEvent> pending = await store.GetPendingEventsAsync(1, cancellationToken);
            if (pending.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                continue;
            }

            PendingBorneEvent item = pending[0];
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!acknowledgements.TryAdd(item.EventId, completion)) continue;
            try
            {
                JsonObject data = JsonNode.Parse(item.PayloadJson)?.AsObject()
                    ?? throw new InvalidDataException("A queued event payload is not a JSON object.");
                data["borneId"] = options.BorneId;
                await SendEnvelopeAsync(socket, sendLock, item.EventType, item.EventId,
                    data, cancellationToken);
                await store.RecordEventAttemptAsync(item.EventId, cancellationToken);
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            finally
            {
                acknowledgements.TryRemove(item.EventId, out _);
            }
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        ConcurrentDictionary<Guid, TaskCompletionSource<bool>> acknowledgements,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? text = await ReceiveTextAsync(socket, cancellationToken);
            if (text is null) throw new WebSocketException("The central peer closed the WebSocket.");
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            ValidateEnvelope(root);
            JsonElement eventElement = root.GetProperty("event");
            string? eventName = eventElement.GetString();
            JsonElement data = root.GetProperty("data");

            switch (eventName)
            {
                case "heartbeat.ack":
                    linkStatus.SetConnected(true);
                    break;
                case "sync.ack":
                    if (data.TryGetProperty("eventId", out JsonElement eventIdElement) &&
                        Guid.TryParse(eventIdElement.GetString(), out Guid eventId))
                    {
                        await store.AcknowledgeEventAsync(eventId, cancellationToken);
                        if (acknowledgements.TryGetValue(eventId, out TaskCompletionSource<bool>? completion))
                            completion.TrySetResult(true);
                    }
                    break;
                case "reservation.snapshot":
                    await ApplyReservationSnapshotAsync(socket, sendLock, data, cancellationToken);
                    break;
                case "reservation.assigned":
                case "reservation.updated":
                    if (data.TryGetProperty("reservation", out JsonElement reservationElement))
                        await ApplyReservationAsync(socket, sendLock, reservationElement, cancellationToken);
                    break;
                case "reservation.cancelled":
                    if (data.TryGetProperty("reservationId", out JsonElement idElement) && idElement.TryGetInt32(out int id))
                    {
                        try { await operations.CancelCachedReservationAsync(id, cancellationToken); }
                        catch (KeyNotFoundException) { }
                        await SendEnvelopeAsync(socket, sendLock, "reservation.cancelled.ack", Guid.NewGuid(),
                            new { borneId = options.BorneId, reservationId = id, applied = true }, cancellationToken);
                    }
                    break;
                case "tariff.configuration":
                    await ApplyTariffAsync(socket, sendLock, data, cancellationToken);
                    break;
                case "tariff.configuration.removed":
                    await RemoveTariffAsync(socket, sendLock, data, cancellationToken);
                    break;
                case "error":
                    throw new WebSocketException("The central API rejected a Borne message.");
            }
        }
    }

    private async Task ApplyReservationSnapshotAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        if (!data.TryGetProperty("reservations", out JsonElement reservations) ||
            reservations.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Reservation snapshot format is invalid.");
        }

        if (reservations.GetArrayLength() > 1)
        {
            throw new InvalidDataException("Central API sent more than one reservation to a single Borne.");
        }

        if (reservations.GetArrayLength() == 1)
        {
            await ApplyReservationAsync(socket, sendLock, reservations[0], cancellationToken, acknowledge: false);
        }
        else
        {
            Reservation? cached = await store.GetCachedReservationAsync(cancellationToken);
            if (cached is not null)
            {
                try { await operations.CancelCachedReservationAsync(cached.IdReservation, cancellationToken); }
                catch (KeyNotFoundException) { }
            }
        }

        await SendEnvelopeAsync(socket, sendLock, "reservation.snapshot.ack", Guid.NewGuid(),
            new { borneId = options.BorneId, count = reservations.GetArrayLength(), applied = true }, cancellationToken);
    }

    private async Task ApplyReservationAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        JsonElement json,
        CancellationToken cancellationToken,
        bool acknowledge = true)
    {
        ReservationWire? wire = json.Deserialize<ReservationWire>(JsonOptions);
        if (wire is null || string.IsNullOrWhiteSpace(wire.Tag))
            throw new InvalidDataException("Reservation update is missing required fields.");
        if (!Enum.TryParse(wire.EnergyType, ignoreCase: false, out TypeEnergie energyType) || !Enum.IsDefined(energyType))
            throw new InvalidDataException("Reservation energy type is invalid.");

        await operations.CacheReservationAsync(new Reservation
        {
            IdReservation = wire.ReservationId,
            RefId = wire.ReferenceId,
            DateDeb = wire.StartsAtUtc.UtcDateTime,
            DateFin = wire.EndsAtUtc.UtcDateTime,
            TypeEnergie = energyType,
            Tag = wire.Tag,
            IdBorne = 1
        }, cancellationToken);
        if (acknowledge)
        {
            await SendEnvelopeAsync(socket, sendLock, "reservation.ack", Guid.NewGuid(),
                new { borneId = options.BorneId, reservationId = wire.ReservationId, applied = true }, cancellationToken);
        }
    }

    private async Task ApplyTariffAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        TariffWire? wire = data.Deserialize<TariffWire>(JsonOptions);
        if (wire is null || !Enum.TryParse(wire.EnergyType, ignoreCase: false, out TypeEnergie energyType) || !Enum.IsDefined(energyType))
            throw new InvalidDataException("Tariff update format is invalid.");
        List<Tranche> bands = wire.Bands.Select(band => new Tranche
        {
            IdTranche = band.Id,
            IdTarif = wire.TarifId,
            ValMin = band.Minimum,
            ValMax = band.Maximum,
            Prix = band.Price
        }).ToList();
        await operations.CacheTariffScheduleAsync(
            new BorneTariffSchedule(new Tarif { IdTarif = wire.TarifId, TypeEnergie = energyType }, bands, wire.Version),
            cancellationToken);
        await SendEnvelopeAsync(socket, sendLock, "tariff.configuration.ack", Guid.NewGuid(),
            new { borneId = options.BorneId, tarifId = wire.TarifId, version = wire.Version, applied = true },
            cancellationToken);
    }

    private async Task RemoveTariffAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        if (!data.TryGetProperty("tarifId", out JsonElement tariffElement) ||
            !tariffElement.TryGetInt32(out int tarifId) || tarifId < 0 ||
            !data.TryGetProperty("energyType", out JsonElement energyElement) ||
            !Enum.TryParse(energyElement.GetString(), ignoreCase: false, out TypeEnergie energyType) ||
            !Enum.IsDefined(energyType))
        {
            throw new InvalidDataException("Tariff removal format is invalid.");
        }

        await operations.DeactivateTariffScheduleAsync(tarifId, energyType, cancellationToken);
        await SendEnvelopeAsync(socket, sendLock, "tariff.configuration.ack", Guid.NewGuid(),
            new { borneId = options.BorneId, tarifId, energyType = energyType.ToString(), removed = true, applied = true },
            cancellationToken);
    }

    private async Task SendEnvelopeAsync(
        ClientWebSocket socket,
        SemaphoreSlim sendLock,
        string eventName,
        Guid messageId,
        object data,
        CancellationToken cancellationToken)
    {
        var envelope = new
        {
            @event = eventName,
            version = 1,
            messageId,
            sentAtUtc = timeProvider.GetUtcNow(),
            data
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static void ValidateEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out JsonElement version) ||
            version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1 ||
            !root.TryGetProperty("event", out JsonElement eventName) ||
            eventName.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(eventName.GetString()) ||
            !root.TryGetProperty("messageId", out JsonElement messageId) ||
            !Guid.TryParse(messageId.GetString(), out _) ||
            !root.TryGetProperty("sentAtUtc", out JsonElement sentAt) ||
            !sentAt.TryGetDateTimeOffset(out _) ||
            !root.TryGetProperty("data", out _))
        {
            throw new InvalidDataException("The central message envelope is invalid.");
        }
    }

    private Uri BuildWebSocketUri()
    {
        var baseUri = new Uri(options.CentralApiBaseUrl!, UriKind.Absolute);
        string scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        string authority = baseUri.IsDefaultPort ? baseUri.Host : $"{baseUri.Host}:{baseUri.Port}";
        return new Uri($"{scheme}://{authority}/ws/bornes/{Uri.EscapeDataString(options.BorneId)}");
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        const int maxBytes = 64 * 1024;
        byte[] buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Only text frames are supported.");
            if (stream.Length + result.Count > maxBytes) throw new InvalidDataException("Central message is too large.");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static TimeSpan WithJitter(TimeSpan delay)
    {
        double factor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromMilliseconds(delay.TotalMilliseconds * factor);
    }

    private sealed record ReservationWire(
        int ReservationId,
        int ReferenceId,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        string EnergyType,
        string? Tag);

    private sealed record TariffWire(
        int TarifId,
        string EnergyType,
        string Version,
        IReadOnlyList<BandWire> Bands);

    private sealed record BandWire(int Id, decimal Minimum, decimal Maximum, decimal Price);
}
