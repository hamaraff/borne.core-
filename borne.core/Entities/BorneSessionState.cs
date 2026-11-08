namespace borne.core.Entities;

/// <summary>Durable session and meter checkpoint for this Borne.</summary>
public sealed record BorneSessionState(
    int ReservationId,
    int TarifId,
    string TarifVersion,
    bool IsPaused,
    decimal LastCumulativeQuantity,
    decimal LastCumulativeAmount,
    DateTimeOffset UpdatedAtUtc);
