using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;
using borne.business.Services;

namespace borne.central.api;

public static class CentralApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCentralApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/bornes/{borneId}/health", GetHealthAsync);
        endpoints.MapGet("/api/bornes/{borneId}/events", GetEventsAsync);
        endpoints.MapGet("/api/tariffs", GetTariffsAsync);
        endpoints.MapPost("/api/tariffs", AddTariffAsync);
        endpoints.MapPut("/api/tariffs/{tarifId:int}", UpdateTariffAsync);
        endpoints.MapDelete("/api/tariffs/{tarifId:int}", DeleteTariffAsync);
        endpoints.MapGet("/api/tariffs/{tarifId:int}/tranches", GetTranchesAsync);
        endpoints.MapPost("/api/tranches", AddTrancheAsync);
        endpoints.MapPut("/api/tranches/{trancheId:int}", UpdateTrancheAsync);
        endpoints.MapDelete("/api/tranches/{trancheId:int}", DeleteTrancheAsync);
        endpoints.MapGet("/ws/bornes/{borneId}", HandleBorneSocketAsync);
        endpoints.MapGet("/ws/monitoring", HandleMonitoringSocketAsync);
        return endpoints;
    }

    private static async Task<IResult> GetHealthAsync(string borneId, HttpContext context, BorneTokenValidator tokens, CentralPersistenceStore store, CancellationToken ct)
    {
        if (!tokens.IsMonitoringTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        BorneHealth? health = await store.GetHealthAsync(borneId, DateTimeOffset.UtcNow, ct);
        return health is null ? Results.NotFound() : Results.Ok(health);
    }

    private static async Task<IResult> GetEventsAsync(string borneId, int? limit, HttpContext context, BorneTokenValidator tokens, CentralPersistenceStore store, CancellationToken ct)
    {
        if (!tokens.IsMonitoringTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        int take = Math.Clamp(limit ?? 100, 1, 500);
        IReadOnlyList<StoredEvent> events = await store.GetEventsAsync(borneId, take, ct);
        return Results.Ok(events.Select(e => new { e.EventId, e.EventType, payload = JsonSerializer.Deserialize<JsonElement>(e.PayloadJson), e.OccurredAtUtc, e.ReceivedAtUtc }));
    }

    private static async Task<IResult> GetTariffsAsync(HttpContext context, BorneTokenValidator tokens, TarifService tariffs)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        return Results.Ok(await tariffs.GetAllTarifsAsync());
    }

    private static async Task<IResult> AddTariffAsync(
        TarifInput request, HttpContext context, BorneTokenValidator tokens,
        TarifService tariffs, TariffConfigurationPublisher publisher, CancellationToken ct)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        TypeEnergie energyType = ParseEnergy(request.EnergyType);
        if ((await tariffs.GetTarifsByEnergieAsync(energyType)).Count != 0)
            throw new InvalidOperationException("A tariff already exists for this energy type.");
        Tarif tariff = new() { TypeEnergie = energyType };
        await tariffs.AddTarifAsync(tariff);
        int updates = await publisher.PublishCurrentAsync(ct);
        return Results.Created($"/api/tariffs/{tariff.IdTarif}", new { tarifId = tariff.IdTarif, configurationFramesSent = updates });
    }

    private static async Task<IResult> UpdateTariffAsync(
        int tarifId, TarifInput request, HttpContext context, BorneTokenValidator tokens,
        TarifService tariffs, TariffConfigurationPublisher publisher, CancellationToken ct)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        TypeEnergie energyType = ParseEnergy(request.EnergyType);
        List<Tarif> all = await tariffs.GetAllTarifsAsync();
        if (all.Any(item => item.IdTarif != tarifId && item.TypeEnergie == energyType))
            throw new InvalidOperationException("A tariff already exists for this energy type.");
        await tariffs.UpdateTarifAsync(new Tarif { IdTarif = tarifId, TypeEnergie = energyType });
        int updates = await publisher.PublishCurrentAsync(ct);
        return Results.Ok(new { tarifId, configurationFramesSent = updates });
    }

    private static async Task<IResult> DeleteTariffAsync(
        int tarifId, HttpContext context, BorneTokenValidator tokens,
        TarifService tariffs, TariffConfigurationPublisher publisher, CancellationToken ct)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        await tariffs.DeleteTarifAsync(tarifId);
        int updates = await publisher.PublishCurrentAsync(ct);
        return Results.Ok(new { tarifId, deleted = true, configurationFramesSent = updates });
    }

    private static async Task<IResult> GetTranchesAsync(
        int tarifId, HttpContext context, BorneTokenValidator tokens, TrancheService tranches, CancellationToken ct)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        return Results.Ok(await tranches.GetTranchesByTarifAsync(tarifId, ct));
    }

    private static async Task<IResult> AddTrancheAsync(
        TrancheInput request, HttpContext context, BorneTokenValidator tokens,
        TrancheService tranches, TariffConfigurationPublisher publisher, CancellationToken ct)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        Tranche tranche = new()
        {
            ValMin = request.Minimum,
            ValMax = request.Maximum,
            Prix = request.Price,
            IdTarif = request.TarifId
        };
        await tranches.AddTrancheAsync(tranche, ct);
        int updates = await publisher.PublishCurrentAsync(ct);
        return Results.Created($"/api/tranches/{tranche.IdTranche}", new { trancheId = tranche.IdTranche, tarifId = tranche.IdTarif, configurationFramesSent = updates });
    }

    private static async Task<IResult> UpdateTrancheAsync(
        int trancheId, TrancheInput request, HttpContext context, BorneTokenValidator tokens,
        TrancheService tranches, TariffConfigurationPublisher publisher, CancellationToken ct)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        await tranches.UpdateTrancheAsync(new Tranche
        {
            IdTranche = trancheId,
            ValMin = request.Minimum,
            ValMax = request.Maximum,
            Prix = request.Price,
            IdTarif = request.TarifId
        }, ct);
        int updates = await publisher.PublishCurrentAsync(ct);
        return Results.Ok(new { trancheId, tarifId = request.TarifId, configurationFramesSent = updates });
    }

    private static async Task<IResult> DeleteTrancheAsync(
        int trancheId, HttpContext context, BorneTokenValidator tokens,
        TrancheService tranches, TariffConfigurationPublisher publisher, CancellationToken ct)
    {
        if (!tokens.IsTariffManagementTokenValid(Bearer(context.Request.Headers.Authorization))) return Results.Unauthorized();
        Tranche tranche = await tranches.GetTrancheByIdAsync(trancheId, ct)
            ?? throw new KeyNotFoundException("The tariff band was not found.");
        await tranches.DeleteTrancheAsync(trancheId, ct);
        int updates = await publisher.PublishCurrentAsync(ct);
        return Results.Ok(new { trancheId, tarifId = tranche.IdTarif, deleted = true, configurationFramesSent = updates });
    }

    private static TypeEnergie ParseEnergy(string value) =>
        Enum.TryParse(value, ignoreCase: false, out TypeEnergie energyType) && Enum.IsDefined(energyType)
            ? energyType
            : throw new ArgumentException("energyType must be Electricite or Eau.");

    public sealed record TarifInput(string EnergyType);
    public sealed record TrancheInput(int TarifId, decimal Minimum, decimal Maximum, decimal Price);

    private static async Task HandleBorneSocketAsync(
        string borneId, HttpContext context, BorneTokenValidator tokens, IConfigDAO configDao,
        CentralPersistenceStore store, CentralRealtimeHub hub,
        BorneRealtimeHub borneHub, TariffConfigurationPublisher tariffPublisher)
    {
        string? token = Bearer(context.Request.Headers.Authorization);
        if (!tokens.IsValid(borneId, token)) { context.Response.StatusCode = 401; return; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        ConfigBorne? configured = await configDao.GetConfigBorneByCodeAsync(borneId, context.RequestAborted);
        if (configured is null) { context.Response.StatusCode = 404; return; }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        BorneRealtimeHub.BornePeer peer = borneHub.Add(borneId, socket);
        try
        {
            await tariffPublisher.SendCurrentToSocketAsync(socket, context.RequestAborted);

            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                string? raw = await ReceiveTextAsync(socket, context.RequestAborted);
                if (raw is null) break;
                using JsonDocument document = JsonDocument.Parse(raw);
                JsonElement root = document.RootElement;
                ValidateEnvelope(root);
                string eventName = root.GetProperty("event").GetString()!;
                Guid messageId = root.GetProperty("messageId").GetGuid();
                JsonElement data = root.GetProperty("data");
                DateTimeOffset received = DateTimeOffset.UtcNow;
                switch (eventName)
                {
                    case "heartbeat":
                        if (!string.Equals(data.GetProperty("borneId").GetString(), borneId, StringComparison.Ordinal))
                            throw new InvalidDataException("Heartbeat Borne identity does not match the authenticated connection.");
                        await store.RecordHeartbeatAsync(borneId, received, context.RequestAborted);
                        await borneHub.SendAsync(socket, "heartbeat.ack", new { borneId, receivedAtUtc = received }, context.RequestAborted);
                        await hub.BroadcastAsync("borne.health", new { borneId, lastHeartbeatUtc = received, isOnline = true }, context.RequestAborted);
                        break;
                    case "consumption.reading":
                    case "session.started":
                    case "session.resumed":
                    case "session.paused":
                    case "session.cancelled":
                        if (!string.Equals(data.GetProperty("borneId").GetString(), borneId, StringComparison.Ordinal))
                            throw new InvalidDataException("Event Borne identity does not match the authenticated connection.");
                        if (eventName == "consumption.reading") ValidateConsumption(messageId, data);
                        DateTimeOffset occurred = data.TryGetProperty("occurredAtUtc", out JsonElement occurredValue) && occurredValue.TryGetDateTimeOffset(out DateTimeOffset eventTime)
                            ? eventTime
                            : data.TryGetProperty("updatedAtUtc", out JsonElement updatedValue) && updatedValue.TryGetDateTimeOffset(out DateTimeOffset updatedTime)
                                ? updatedTime
                                : root.GetProperty("sentAtUtc").GetDateTimeOffset();
                        StoredEventResult result = await store.PersistEventAsync(messageId, borneId, eventName, data.GetRawText(), occurred, received, context.RequestAborted);
                        await borneHub.SendAsync(socket, "sync.ack", new { eventId = messageId }, context.RequestAborted);
                        if (result.Accepted)
                            await hub.BroadcastAsync("borne.event.persisted", new { borneId, eventId = messageId, eventType = eventName, data }, context.RequestAborted);
                        break;
                    case "reservation.snapshot.ack":
                    case "reservation.ack":
                    case "reservation.cancelled.ack":
                    case "tariff.configuration.ack":
                        // These acknowledge that the Borne durably cached central configuration.
                        break;
                    default:
                        throw new InvalidDataException("Unsupported Borne event.");
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception) when (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Invalid or unavailable Borne operation.", CancellationToken.None); }
            catch (WebSocketException) { }
        }
        finally
        {
            borneHub.Remove(peer);
        }
    }

    private static async Task HandleMonitoringSocketAsync(HttpContext context, BorneTokenValidator tokens, CentralRealtimeHub hub, CentralPersistenceStore store)
    {
        if (!tokens.IsMonitoringTokenValid(Bearer(context.Request.Headers.Authorization))) { context.Response.StatusCode = 401; return; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        CentralRealtimeHub.MonitorPeer peer = hub.Add(socket);
        byte[] buffer = new byte[2048];
        try
        {
            IReadOnlyList<StoredEvent> history = await store.GetRecentEventsAsync(500, context.RequestAborted);
            IReadOnlyList<BorneHealth> health = await store.GetAllHealthAsync(DateTimeOffset.UtcNow, context.RequestAborted);
            await hub.SendAsync(peer, "monitoring.replay", new
            {
                events = history.Select(e => new { e.EventId, e.BorneId, e.EventType, payload = JsonSerializer.Deserialize<JsonElement>(e.PayloadJson), e.OccurredAtUtc, e.ReceivedAtUtc }),
                health
            }, context.RequestAborted);
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.MessageType != WebSocketMessageType.Text) { await socket.CloseOutputAsync(WebSocketCloseStatus.InvalidMessageType, "Text frames only.", CancellationToken.None); break; }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (WebSocketException) { }
        finally { hub.Remove(peer); }
    }

    private static string? Bearer(string? header) => header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? header[7..].Trim() : null;

    private static void ValidateConsumption(Guid eventId, JsonElement data)
    {
        if (!data.TryGetProperty("readingId", out JsonElement readingIdValue) ||
            !readingIdValue.TryGetGuid(out Guid readingId) || readingId != eventId ||
            !data.TryGetProperty("reservationId", out JsonElement reservationValue) ||
            !reservationValue.TryGetInt32(out int reservationId) || reservationId <= 0 ||
            !data.TryGetProperty("tarifId", out JsonElement tarifValue) ||
            !tarifValue.TryGetInt32(out int tarifId) || tarifId <= 0 ||
            !data.TryGetProperty("occurredAtUtc", out JsonElement occurredValue) ||
            !occurredValue.TryGetDateTimeOffset(out _) ||
            !data.TryGetProperty("cumulativeQuantity", out JsonElement quantityValue) ||
            !quantityValue.TryGetDecimal(out decimal quantity) || quantity <= 0 ||
            !data.TryGetProperty("amount", out JsonElement amountValue) ||
            !amountValue.TryGetDecimal(out decimal amount) || amount < 0 ||
            !data.TryGetProperty("cumulativeAmount", out JsonElement totalValue) ||
            !totalValue.TryGetDecimal(out decimal total) || total < 0 ||
            !data.TryGetProperty("tariffVersion", out JsonElement versionValue) ||
            versionValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(versionValue.GetString()))
        {
            throw new InvalidDataException("The cumulative consumption reading does not match the central persistence contract.");
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
    {
        const int maxBytes = 64 * 1024;
        byte[] buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Text frames only.");
            if (stream.Length + result.Count > maxBytes) throw new InvalidDataException("Message too large.");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static void ValidateEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out JsonElement version) || !version.TryGetInt32(out int v) || v != 1 ||
            !root.TryGetProperty("event", out JsonElement name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) ||
            !root.TryGetProperty("messageId", out JsonElement id) || !id.TryGetGuid(out Guid guid) || guid == Guid.Empty ||
            !root.TryGetProperty("sentAtUtc", out JsonElement sent) || !sent.TryGetDateTimeOffset(out _) ||
            !root.TryGetProperty("data", out _)) throw new InvalidDataException("Invalid envelope.");
    }
}
