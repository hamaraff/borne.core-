using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;

namespace borne.business.Services;

/// <summary>Runs one Borne from its local durable state, without central API calls.</summary>
public sealed class BorneOperationService(
    IBorneLocalStore store,
    ICardTagHasher cardTagHasher,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim stateGate = new(1, 1);

    public Task<bool> IsStorageAvailableAsync(CancellationToken cancellationToken = default) =>
        store.IsAvailableAsync(cancellationToken);

    public Task<Reservation?> GetCachedReservationAsync(CancellationToken cancellationToken = default) =>
        store.GetCachedReservationAsync(cancellationToken);

    public Task CancelCachedReservationAsync(
        int reservationId,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CancelCachedReservationCoreAsync(reservationId, cancellationToken), cancellationToken);

    private async Task CancelCachedReservationCoreAsync(
        int reservationId,
        CancellationToken cancellationToken)
    {
        Reservation? reservation = await store.GetCachedReservationAsync(cancellationToken);
        if (reservation is null || reservation.IdReservation != reservationId)
        {
            throw new KeyNotFoundException("The reservation is not cached on this Borne.");
        }

        BorneSessionState? session = await store.GetSessionAsync(cancellationToken);
        if (session is not null && session.ReservationId == reservationId && !session.IsPaused)
        {
            BorneSessionState cancelled = session with
            {
                IsPaused = true,
                UpdatedAtUtc = timeProvider.GetUtcNow()
            };
            await QueueSessionStateAsync(cancelled, "session.cancelled", cancellationToken);
        }

        await store.ClearReservationAsync(cancellationToken);
    }

    public Task CacheReservationAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CacheReservationCoreAsync(reservation, cancellationToken), cancellationToken);

    private async Task CacheReservationCoreAsync(
        Reservation reservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateReservation(reservation);

        Reservation? existing = await store.GetCachedReservationAsync(cancellationToken);
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (existing is not null &&
            existing.IdReservation != reservation.IdReservation &&
            existing.DateFin > nowUtc)
        {
            throw new InvalidOperationException(
                "This Borne already has a cached reservation that has not expired.");
        }

        Reservation localCopy = CopyReservation(reservation);
        localCopy.Tag = cardTagHasher.Hash(reservation.Tag!);
        await store.CacheReservationAsync(localCopy, cancellationToken);
    }

    public Task CacheTariffScheduleAsync(
        BorneTariffSchedule schedule,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CacheTariffScheduleCoreAsync(schedule, cancellationToken), cancellationToken);

    public Task DeactivateTariffScheduleAsync(
        int tarifId,
        TypeEnergie energyType,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => store.DeactivateTariffScheduleAsync(tarifId, energyType, cancellationToken), cancellationToken);

    private async Task CacheTariffScheduleCoreAsync(
        BorneTariffSchedule schedule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Tarif.IdTarif <= 0 || !Enum.IsDefined(schedule.Tarif.TypeEnergie))
        {
            throw new ArgumentException("The tariff configuration is invalid.", nameof(schedule));
        }

        if (string.IsNullOrWhiteSpace(schedule.Version))
        {
            throw new ArgumentException("A tariff configuration version is required.", nameof(schedule));
        }

        _ = TariffCalculator.Calculate(schedule, 0m);
        await store.CacheTariffScheduleAsync(schedule, cancellationToken);
    }

    public Task<BorneSessionState> AuthorizeAndStartAsync(
        string cardTag,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => AuthorizeAndStartCoreAsync(cardTag, cancellationToken), cancellationToken);

    private async Task<BorneSessionState> AuthorizeAndStartCoreAsync(
        string cardTag,
        CancellationToken cancellationToken)
    {
        Reservation reservation = await RequireValidReservationAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(cardTag) || string.IsNullOrWhiteSpace(reservation.Tag))
        {
            throw new UnauthorizedAccessException("The card is not authorized for this reservation.");
        }

        byte[] presentedHash = Encoding.UTF8.GetBytes(cardTagHasher.Hash(cardTag));
        byte[] storedHash = Encoding.UTF8.GetBytes(reservation.Tag);
        if (presentedHash.Length != storedHash.Length ||
            !CryptographicOperations.FixedTimeEquals(presentedHash, storedHash))
        {
            throw new UnauthorizedAccessException("The card is not authorized for this reservation.");
        }

        BorneSessionState? existingSession = await store.GetSessionAsync(cancellationToken);
        if (existingSession is not null && existingSession.ReservationId == reservation.IdReservation)
        {
            if (!existingSession.IsPaused)
            {
                throw new InvalidOperationException("A session for this reservation is already running.");
            }

            BorneTariffSchedule? cachedSchedule = await store.GetTariffScheduleAsync(
                reservation.TypeEnergie,
                existingSession.TarifVersion,
                cancellationToken);
            if (cachedSchedule is null || cachedSchedule.Tarif.IdTarif != existingSession.TarifId)
            {
                throw new InvalidOperationException("The session tariff snapshot is unavailable locally.");
            }

            BorneSessionState resumed = existingSession with
            {
                IsPaused = false,
                UpdatedAtUtc = timeProvider.GetUtcNow()
            };
            await QueueSessionStateAsync(resumed, "session.resumed", cancellationToken);
            return resumed;
        }

        BorneTariffSchedule schedule = await store.GetTariffScheduleAsync(
            reservation.TypeEnergie,
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("No tariff schedule is cached for this reservation.");

        var session = new BorneSessionState(
            reservation.IdReservation,
            schedule.Tarif.IdTarif,
            schedule.Version,
            false,
            0m,
            0m,
            timeProvider.GetUtcNow());
        await QueueSessionStateAsync(session, "session.started", cancellationToken);
        return session;
    }

    public Task<BorneSessionState> PauseSessionAsync(
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => PauseSessionCoreAsync(cancellationToken), cancellationToken);

    private async Task<BorneSessionState> PauseSessionCoreAsync(CancellationToken cancellationToken)
    {
        BorneSessionState session = await store.GetSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("No session is running.");
        if (session.IsPaused)
        {
            return session;
        }

        BorneSessionState paused = session with
        {
            IsPaused = true,
            UpdatedAtUtc = timeProvider.GetUtcNow()
        };
        await QueueSessionStateAsync(paused, "session.paused", cancellationToken);
        return paused;
    }

    public Task<BorneSessionState> RecordCumulativeReadingAsync(
        decimal cumulativeQuantity,
        Guid? readingId = null,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () => RecordCumulativeReadingCoreAsync(cumulativeQuantity, readingId, cancellationToken),
            cancellationToken);

    private async Task<BorneSessionState> RecordCumulativeReadingCoreAsync(
        decimal cumulativeQuantity,
        Guid? readingId,
        CancellationToken cancellationToken)
    {
        if (cumulativeQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cumulativeQuantity));
        }

        Guid id = readingId ?? Guid.NewGuid();
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A reading ID is required.", nameof(readingId));
        }

        Consommation? duplicate = await store.GetReadingByIdAsync(id, cancellationToken);
        if (duplicate is not null)
        {
            BorneSessionState? currentSession = await store.GetSessionAsync(cancellationToken);
            if (duplicate.Quantite != cumulativeQuantity ||
                currentSession?.ReservationId != duplicate.ReservationId)
            {
                throw new InvalidOperationException("A reading ID was reused with different data.");
            }

            return currentSession;
        }

        BorneSessionState session = await store.GetSessionAsync(cancellationToken)
            ?? throw new InvalidOperationException("No session is running.");
        if (session.IsPaused)
        {
            throw new InvalidOperationException("A paused session cannot record consumption.");
        }

        if (cumulativeQuantity <= session.LastCumulativeQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cumulativeQuantity),
                "The cumulative meter reading must increase.");
        }

        Reservation reservation = await RequireValidReservationAsync(cancellationToken);
        if (reservation.IdReservation != session.ReservationId)
        {
            throw new InvalidOperationException("The active session no longer matches the cached reservation.");
        }

        BorneTariffSchedule schedule = await store.GetTariffScheduleAsync(
            reservation.TypeEnergie,
            session.TarifVersion,
            cancellationToken)
            ?? throw new InvalidOperationException("The session tariff snapshot is unavailable locally.");
        decimal cumulativeAmount = TariffCalculator.Calculate(schedule, cumulativeQuantity);
        decimal amountForThisReading = cumulativeAmount - session.LastCumulativeAmount;
        if (amountForThisReading < 0)
        {
            throw new InvalidOperationException("The configured tariff would reduce the cumulative amount.");
        }

        DateTimeOffset occurredAtUtc = timeProvider.GetUtcNow();
        var reading = new Consommation
        {
            ReadingId = id,
            ReservationId = reservation.IdReservation,
            TarifId = schedule.Tarif.IdTarif,
            Date = occurredAtUtc.UtcDateTime,
            Quantite = cumulativeQuantity,
            Montant = amountForThisReading,
            MontantCumule = cumulativeAmount
        };
        BorneSessionState updated = session with
        {
            LastCumulativeQuantity = cumulativeQuantity,
            LastCumulativeAmount = cumulativeAmount,
            UpdatedAtUtc = occurredAtUtc
        };
        var payload = new ConsumptionSyncPayload(
            id,
            reservation.IdReservation,
            occurredAtUtc,
            cumulativeQuantity,
            amountForThisReading,
            cumulativeAmount,
            schedule.Tarif.IdTarif,
            schedule.Version);
        await store.SaveReadingAndQueueEventAsync(
            reading,
            updated,
            "consumption.reading",
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);
        return updated;
    }

    private async Task QueueSessionStateAsync(
        BorneSessionState session,
        string eventType,
        CancellationToken cancellationToken)
    {
        Guid eventId = Guid.NewGuid();
        var payload = new SessionSyncPayload(
            eventId,
            session.ReservationId,
            session.IsPaused,
            session.LastCumulativeQuantity,
            session.LastCumulativeAmount,
            session.UpdatedAtUtc);
        await store.SaveSessionAndQueueEventAsync(
            session,
            eventId,
            eventType,
            JsonSerializer.Serialize(payload, JsonOptions),
            session.UpdatedAtUtc,
            cancellationToken);
    }

    private async Task RunExclusiveAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await stateGate.WaitAsync(cancellationToken);
        try { await action(); }
        finally { stateGate.Release(); }
    }

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await stateGate.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { stateGate.Release(); }
    }

    private async Task<Reservation> RequireValidReservationAsync(CancellationToken cancellationToken)
    {
        Reservation reservation = await store.GetCachedReservationAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No reservation is cached on this Borne.");
        DateTime nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (reservation.DateDeb > nowUtc || reservation.DateFin <= nowUtc)
        {
            throw new InvalidOperationException("The cached reservation is not currently valid.");
        }

        return reservation;
    }

    private static void ValidateReservation(Reservation reservation)
    {
        if (reservation.IdReservation <= 0 || reservation.IdBorne <= 0 || reservation.RefId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservation), "Reservation IDs must be positive.");
        }

        if (reservation.DateDeb >= reservation.DateFin || string.IsNullOrWhiteSpace(reservation.Tag))
        {
            throw new ArgumentException("The reservation interval and card tag are required.", nameof(reservation));
        }

        if (!Enum.IsDefined(reservation.TypeEnergie))
        {
            throw new ArgumentOutOfRangeException(nameof(reservation.TypeEnergie));
        }
    }

    private static Reservation CopyReservation(Reservation reservation) => new()
    {
        IdReservation = reservation.IdReservation,
        RefId = reservation.RefId,
        DateDeb = reservation.DateDeb,
        DateFin = reservation.DateFin,
        TypeEnergie = reservation.TypeEnergie,
        Tag = reservation.Tag,
        IdBorne = reservation.IdBorne
    };

    private sealed record ConsumptionSyncPayload(
        Guid ReadingId,
        int ReservationId,
        DateTimeOffset OccurredAtUtc,
        decimal CumulativeQuantity,
        decimal Amount,
        decimal CumulativeAmount,
        int TarifId,
        string TariffVersion);

    private sealed record SessionSyncPayload(
        Guid EventId,
        int ReservationId,
        bool IsPaused,
        decimal CumulativeQuantity,
        decimal CumulativeAmount,
        DateTimeOffset UpdatedAtUtc);
}
