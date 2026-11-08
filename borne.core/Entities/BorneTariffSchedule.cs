namespace borne.core.Entities;

/// <summary>A tariff and the progressive bands cached by one Borne.</summary>
public sealed record BorneTariffSchedule(
    Tarif Tarif,
    IReadOnlyList<Tranche> Tranches,
    string Version);
