namespace borne.core.Entities;

/// <summary>An event durably queued for delivery to the central persistence API.</summary>
public sealed record PendingBorneEvent(
    Guid EventId,
    string EventType,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc,
    int Attempts);
