using System.Text.Json;
using borne.business.Services;
using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;

namespace borne.device.api;

public sealed record ReservationCacheRequest(
    int ReservationId,
    int ReferenceId,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    string EnergyType,
    string CardTag);

public sealed record TariffCacheRequest(
    int TarifId,
    string EnergyType,
    string Version,
    IReadOnlyList<TariffBandInput> Bands);

public sealed record TariffBandInput(int Id, decimal Minimum, decimal Maximum, decimal Price);
public sealed record SessionStartRequest(string CardTag);
public sealed record ConsumptionReadingRequest(Guid? ReadingId, decimal CumulativeQuantity);

public static class LocalApiEndpoints
{
    public static IEndpointRouteBuilder MapBorneLocalApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/borne/status", GetStatusAsync);
        endpoints.MapGet("/api/reservations/cached", GetCachedReservationAsync);
        endpoints.MapPut("/api/reservations/cache", CacheReservationAsync);
        endpoints.MapDelete("/api/reservations/cache/{reservationId:int}", ClearReservationAsync);
        endpoints.MapPut("/api/tariffs/cache", CacheTariffAsync);
        endpoints.MapGet("/api/sessions/current", GetSessionAsync);
        endpoints.MapPost("/api/sessions/start", StartSessionAsync);
        endpoints.MapPost("/api/sessions/pause", PauseSessionAsync);
        endpoints.MapPost("/api/sessions/resume", ResumeSessionAsync);
        endpoints.MapPost("/api/consumption/readings", RecordReadingAsync);
        endpoints.MapGet("/api/consumption/current", GetLatestReadingAsync);
        endpoints.MapGet("/api/sync/pending", GetPendingCountAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        BorneRuntimeOptions options,
        IBorneLocalStore store,
        CentralLinkStatus central,
        CancellationToken cancellationToken)
    {
        Reservation? reservation = await store.GetCachedReservationAsync(cancellationToken);
        BorneSessionState? session = await store.GetSessionAsync(cancellationToken);
        return Results.Ok(new
        {
            borneId = options.BorneId,
            localStorage = "ready",
            centralConnected = central.IsConnected,
            centralLastConnectedAtUtc = central.LastConnectedAtUtc,
            cachedReservationId = reservation?.IdReservation,
            reservationValid = reservation is not null &&
                reservation.DateDeb <= DateTime.UtcNow && DateTime.UtcNow < reservation.DateFin,
            session = session is null ? null : new
            {
                session.ReservationId,
                session.IsPaused,
                session.LastCumulativeQuantity,
                session.LastCumulativeAmount
            }
        });
    }

    private static async Task<IResult> GetCachedReservationAsync(
        IBorneLocalStore store,
        CancellationToken cancellationToken)
    {
        Reservation? reservation = await store.GetCachedReservationAsync(cancellationToken);
        return reservation is null ? Results.NotFound() : Results.Ok(ToOutput(reservation));
    }

    private static async Task<IResult> CacheReservationAsync(
        ReservationCacheRequest request,
        BorneRuntimeOptions options,
        BorneOperationService operations,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(request.EnergyType, ignoreCase: false, out TypeEnergie energyType) ||
            !Enum.IsDefined(energyType))
        {
            throw new ArgumentException("energyType must be Electricite or Eau.");
        }

        var reservation = new Reservation
        {
            IdReservation = request.ReservationId,
            RefId = request.ReferenceId,
            DateDeb = request.StartsAtUtc.UtcDateTime,
            DateFin = request.EndsAtUtc.UtcDateTime,
            TypeEnergie = energyType,
            Tag = request.CardTag,
            IdBorne = 1
        };
        await operations.CacheReservationAsync(reservation, cancellationToken);
        return Results.Ok(new { reservationId = request.ReservationId, borneId = options.BorneId, cached = true });
    }

    private static async Task<IResult> ClearReservationAsync(
        int reservationId,
        BorneOperationService operations,
        CancellationToken cancellationToken)
    {
        await operations.CancelCachedReservationAsync(reservationId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> CacheTariffAsync(
        TariffCacheRequest request,
        BorneOperationService operations,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(request.EnergyType, ignoreCase: false, out TypeEnergie energyType) ||
            !Enum.IsDefined(energyType))
        {
            throw new ArgumentException("energyType must be Electricite or Eau.");
        }

        var tariff = new Tarif { IdTarif = request.TarifId, TypeEnergie = energyType };
        List<Tranche> bands = request.Bands.Select(band => new Tranche
        {
            IdTranche = band.Id,
            IdTarif = request.TarifId,
            ValMin = band.Minimum,
            ValMax = band.Maximum,
            Prix = band.Price
        }).ToList();
        await operations.CacheTariffScheduleAsync(
            new BorneTariffSchedule(tariff, bands, request.Version),
            cancellationToken);
        return Results.Ok(new { tarifId = request.TarifId, version = request.Version, cached = true });
    }

    private static async Task<IResult> GetSessionAsync(
        IBorneLocalStore store,
        CancellationToken cancellationToken)
    {
        BorneSessionState? session = await store.GetSessionAsync(cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(session);
    }

    private static async Task<IResult> StartSessionAsync(
        SessionStartRequest request,
        BorneOperationService operations,
        CancellationToken cancellationToken) =>
        Results.Ok(await operations.AuthorizeAndStartAsync(request.CardTag, cancellationToken));

    private static async Task<IResult> PauseSessionAsync(
        BorneOperationService operations,
        CancellationToken cancellationToken) =>
        Results.Ok(await operations.PauseSessionAsync(cancellationToken));

    private static async Task<IResult> ResumeSessionAsync(
        SessionStartRequest request,
        BorneOperationService operations,
        CancellationToken cancellationToken) =>
        Results.Ok(await operations.AuthorizeAndStartAsync(request.CardTag, cancellationToken));

    private static async Task<IResult> RecordReadingAsync(
        ConsumptionReadingRequest request,
        BorneOperationService operations,
        CancellationToken cancellationToken)
    {
        BorneSessionState session = await operations.RecordCumulativeReadingAsync(
            request.CumulativeQuantity,
            request.ReadingId,
            cancellationToken);
        return Results.Accepted("/api/consumption/current", new
        {
            session.ReservationId,
            session.LastCumulativeQuantity,
            session.LastCumulativeAmount,
            session.UpdatedAtUtc
        });
    }

    private static async Task<IResult> GetLatestReadingAsync(
        IBorneLocalStore store,
        CancellationToken cancellationToken)
    {
        Consommation? reading = await store.GetLatestReadingAsync(cancellationToken);
        return reading is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                reading.ReadingId,
                reading.ReservationId,
                occurredAtUtc = new DateTimeOffset(DateTime.SpecifyKind(reading.Date, DateTimeKind.Utc)),
                cumulativeQuantity = reading.Quantite,
                amount = reading.Montant,
                cumulativeAmount = reading.MontantCumule
            });
    }

    private static async Task<IResult> GetPendingCountAsync(
        IBorneLocalStore store,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingBorneEvent> pending = await store.GetPendingEventsAsync(1000, cancellationToken);
        return Results.Ok(new { count = pending.Count });
    }

    private static object ToOutput(Reservation reservation) => new
    {
        reservationId = reservation.IdReservation,
        referenceId = reservation.RefId,
        startsAtUtc = new DateTimeOffset(DateTime.SpecifyKind(reservation.DateDeb, DateTimeKind.Utc)),
        endsAtUtc = new DateTimeOffset(DateTime.SpecifyKind(reservation.DateFin, DateTimeKind.Utc)),
        energyType = reservation.TypeEnergie.ToString()
    };
}
