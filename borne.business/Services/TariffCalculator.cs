using borne.core.Entities;

namespace borne.business.Services;

/// <summary>Calculates a progressive price from a cumulative meter value.</summary>
public static class TariffCalculator
{
    public static decimal Calculate(BorneTariffSchedule schedule, decimal cumulativeQuantity)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (cumulativeQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cumulativeQuantity));
        }

        List<Tranche> bands = ValidateAndOrder(schedule);
        if (cumulativeQuantity > bands[^1].ValMax)
        {
            throw new InvalidOperationException("Cumulative consumption exceeds the configured tariff bands.");
        }

        decimal amount = 0m;
        foreach (Tranche band in bands)
        {
            decimal quantityInBand = Math.Max(
                0m,
                Math.Min(cumulativeQuantity, band.ValMax) - band.ValMin);
            amount += quantityInBand * band.Prix;
            if (cumulativeQuantity <= band.ValMax)
            {
                return amount;
            }
        }

        return amount;
    }

    private static List<Tranche> ValidateAndOrder(BorneTariffSchedule schedule)
    {
        List<Tranche> bands = schedule.Tranches.OrderBy(band => band.ValMin).ToList();
        if (bands.Count == 0)
            throw new InvalidOperationException("No tariff bands are configured.");

        decimal expectedMinimum = 0m;
        foreach (Tranche band in bands)
        {
            if (band.IdTarif != schedule.Tarif.IdTarif ||
                band.ValMin != expectedMinimum ||
                band.ValMax <= band.ValMin ||
                band.Prix <= 0)
            {
                throw new InvalidOperationException(
                    "Tariff bands must be valid, contiguous, and start at zero.");
            }

            expectedMinimum = band.ValMax;
        }

        return bands;
    }
}
