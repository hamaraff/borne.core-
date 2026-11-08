using borne.core.Entities;
using borne.core.Enums;

namespace borne.core.IInfra;

/// <summary>Local durable state owned by a single Borne instance.</summary>
public interface IBorneLocalStore
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<Reservation?> GetCachedReservationAsync(CancellationToken cancellationToken = default);
    Task CacheReservationAsync(Reservation reservation, CancellationToken cancellationToken = default);
    Task ClearReservationAsync(CancellationToken cancellationToken = default);
    Task<BorneTariffSchedule?> GetTariffScheduleAsync(
        TypeEnergie energyType,
        string? version = null,
        CancellationToken cancellationToken = default);
    Task CacheTariffScheduleAsync(
        BorneTariffSchedule schedule,
        CancellationToken cancellationToken = default);
    Task DeactivateTariffScheduleAsync(
        int tarifId,
        TypeEnergie energyType,
        CancellationToken cancellationToken = default);
    Task<BorneSessionState?> GetSessionAsync(CancellationToken cancellationToken = default);
    Task SaveSessionAsync(BorneSessionState session, CancellationToken cancellationToken = default);
    Task SaveSessionAndQueueEventAsync(
        BorneSessionState session,
        Guid eventId,
        string eventType,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);
    Task<Consommation?> GetLatestReadingAsync(CancellationToken cancellationToken = default);
    Task<Consommation?> GetReadingByIdAsync(
        Guid readingId,
        CancellationToken cancellationToken = default);
    Task SaveReadingAndQueueEventAsync(
        Consommation reading,
        BorneSessionState session,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default);
    Task QueueEventAsync(
        Guid eventId,
        string eventType,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingBorneEvent>> GetPendingEventsAsync(
        int limit,
        CancellationToken cancellationToken = default);
    Task AcknowledgeEventAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task RecordEventAttemptAsync(Guid eventId, CancellationToken cancellationToken = default);
}

/// <summary>Hashes card credentials before a reservation is stored locally.</summary>
public interface ICardTagHasher
{
    string Hash(string cardTag);
}
