using borne.business.Services;
using borne.core.Entities;
using borne.core.Enums;

namespace borne.central.api;

public sealed class TariffConfigurationPublisher(
    TarifService tariffs,
    TrancheService tranches,
    BorneRealtimeHub bornes)
{
    public async Task<int> PublishCurrentAsync(CancellationToken cancellationToken)
    {
        List<Tarif> allTariffs = await tariffs.GetAllTarifsAsync();
        int delivered = 0;
        HashSet<TypeEnergie> activeTypes = new();
        foreach (IGrouping<TypeEnergie, Tarif> group in allTariffs.GroupBy(tarif => tarif.TypeEnergie))
        {
            Tarif[] matches = group.ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Configure exactly one tariff per energy type before synchronizing tariffs.");
            TariffConfiguration? configuration = await GetConfigurationAsync(matches[0].IdTarif, cancellationToken);
            if (configuration is not null)
            {
                activeTypes.Add(matches[0].TypeEnergie);
                delivered += await bornes.BroadcastAsync("tariff.configuration", configuration, cancellationToken);
            }
        }

        foreach (TypeEnergie energyType in Enum.GetValues<TypeEnergie>().Where(type => !activeTypes.Contains(type)))
        {
            delivered += await PublishRemovalAsync(0, energyType, cancellationToken);
        }

        return delivered;
    }

    public async Task<int> PublishTariffAsync(int tarifId, CancellationToken cancellationToken)
    {
        TariffConfiguration? configuration = await GetConfigurationAsync(tarifId, cancellationToken);
        if (configuration is null) return 0;
        return await bornes.BroadcastAsync("tariff.configuration", configuration, cancellationToken);
    }

    public async Task SendCurrentToSocketAsync(System.Net.WebSockets.WebSocket socket, CancellationToken cancellationToken)
    {
        List<Tarif> allTariffs = await tariffs.GetAllTarifsAsync();
        HashSet<TypeEnergie> activeTypes = new();
        foreach (IGrouping<TypeEnergie, Tarif> group in allTariffs.GroupBy(tarif => tarif.TypeEnergie))
        {
            Tarif[] matches = group.ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Configure exactly one tariff per energy type before synchronizing tariffs.");
            TariffConfiguration? configuration = await GetConfigurationAsync(matches[0].IdTarif, cancellationToken);
            if (configuration is not null)
            {
                activeTypes.Add(matches[0].TypeEnergie);
                await bornes.SendAsync(socket, "tariff.configuration", configuration, cancellationToken);
            }
        }

        foreach (TypeEnergie energyType in Enum.GetValues<TypeEnergie>().Where(type => !activeTypes.Contains(type)))
            await bornes.SendAsync(socket, "tariff.configuration.removed", new { tarifId = 0, energyType = energyType.ToString() }, cancellationToken);
    }

    public Task<int> PublishRemovalAsync(int tarifId, TypeEnergie energyType, CancellationToken cancellationToken) =>
        bornes.BroadcastAsync("tariff.configuration.removed", new { tarifId, energyType = energyType.ToString() }, cancellationToken);

    private async Task<TariffConfiguration?> GetConfigurationAsync(int tarifId, CancellationToken cancellationToken)
    {
        Tarif tariff = (await tariffs.GetAllTarifsAsync()).SingleOrDefault(item => item.IdTarif == tarifId)
            ?? throw new KeyNotFoundException("The tariff was not found.");
        List<Tranche> bands = await tranches.GetTranchesByTarifAsync(tarifId, cancellationToken);
        if (bands.Count == 0) return null;
        try
        {
            _ = TariffCalculator.Calculate(new BorneTariffSchedule(tariff, bands, "validation"), 0m);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        byte[] canonical = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(bands
            .OrderBy(band => band.ValMin)
            .Select(band => new { band.IdTranche, band.ValMin, band.ValMax, band.Prix }));
        string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(canonical))[..16].ToLowerInvariant();
        return new TariffConfiguration(tarifId, tariff.TypeEnergie.ToString(), $"tarif-{tarifId}-{fingerprint}",
            bands.Select(band => new TariffBandConfiguration(band.IdTranche, band.ValMin, band.ValMax, band.Prix)).ToArray());
    }

    private sealed record TariffConfiguration(int TarifId, string EnergyType, string Version, IReadOnlyList<TariffBandConfiguration> Bands);
    private sealed record TariffBandConfiguration(int Id, decimal Minimum, decimal Maximum, decimal Price);
}
