using borne.core.Entities;
using borne.core.IInfra;

namespace borne.business.Services;

public class TrancheService
{
    private readonly ITrancheDAO trancheDAO;
    private readonly ITarifDAO tarifDAO;

    public TrancheService(ITrancheDAO trancheDAO, ITarifDAO tarifDAO)
    {
        this.trancheDAO = trancheDAO;
        this.tarifDAO = tarifDAO;
    }

    public Task<List<Tranche>> GetAllTranchesAsync(
        CancellationToken cancellationToken = default)
    {
        return trancheDAO.GetAllAsync(cancellationToken);
    }

    public Task<Tranche?> GetTrancheByIdAsync(
        int idTranche,
        CancellationToken cancellationToken = default)
    {
        ValidateId(idTranche, nameof(idTranche));
        return trancheDAO.GetByIdAsync(idTranche, cancellationToken);
    }

    public async Task<List<Tranche>> GetTranchesByTarifAsync(
        int tarifId,
        CancellationToken cancellationToken = default)
    {
        await RequireTarifAsync(tarifId, cancellationToken);
        return await trancheDAO.GetByTarifIdAsync(tarifId, cancellationToken);
    }

    public async Task AddTrancheAsync(
        Tranche tranche,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tranche);
        if (tranche.IdTranche != 0)
        {
            throw new ArgumentException("A new tariff band must not have a database ID.");
        }

        ValidateRangeAndPrice(tranche);
        await RequireTarifAsync(tranche.IdTarif, cancellationToken);
        await ValidateNoOverlapAsync(tranche, null, cancellationToken);
        await trancheDAO.AddAsync(tranche, cancellationToken);
    }

    public async Task UpdateTrancheAsync(
        Tranche tranche,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tranche);
        ValidateId(tranche.IdTranche, nameof(tranche.IdTranche));
        ValidateRangeAndPrice(tranche);
        await RequireTarifAsync(tranche.IdTarif, cancellationToken);
        if (await GetTrancheByIdAsync(tranche.IdTranche, cancellationToken) is null)
        {
            throw new KeyNotFoundException("The tariff band was not found.");
        }

        await ValidateNoOverlapAsync(tranche, tranche.IdTranche, cancellationToken);
        await trancheDAO.UpdateAsync(tranche, cancellationToken);
    }

    public async Task DeleteTrancheAsync(
        int idTranche,
        CancellationToken cancellationToken = default)
    {
        Tranche tranche = await GetTrancheByIdAsync(idTranche, cancellationToken)
            ?? throw new KeyNotFoundException("The tariff band was not found.");
        await trancheDAO.DeleteAsync(tranche, cancellationToken);
    }

    public Task DeleteTrancheAsync(
        Tranche tranche,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tranche);
        return DeleteTrancheAsync(tranche.IdTranche, cancellationToken);
    }

    public async Task<decimal> CalculerMontantAsync(
        int tarifId,
        decimal consommationCumulee,
        CancellationToken cancellationToken = default)
    {
        if (consommationCumulee < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consommationCumulee),
                "Consumption cannot be negative.");
        }

        await RequireTarifAsync(tarifId, cancellationToken);
        if (consommationCumulee == 0)
        {
            return 0m;
        }

        List<Tranche> tranches = await trancheDAO.GetByTarifIdAsync(
            tarifId,
            cancellationToken);
        tranches = tranches.OrderBy(tranche => tranche.ValMin).ToList();

        if (tranches.Count == 0)
        {
            throw new InvalidOperationException("No tariff bands are configured for this tariff.");
        }

        decimal amount = 0m;
        decimal previousLimit = 0m;
        foreach (Tranche tranche in tranches)
        {
            if (tranche.ValMin != previousLimit ||
                tranche.ValMax <= tranche.ValMin ||
                tranche.Prix <= 0)
            {
                throw new InvalidOperationException(
                    "Tariff bands must be valid, non-overlapping, and contiguous from zero.");
            }

            decimal quantityInBand = Math.Max(
                0m,
                Math.Min(consommationCumulee, tranche.ValMax) - tranche.ValMin);
            amount += quantityInBand * tranche.Prix;
            previousLimit = tranche.ValMax;

            if (consommationCumulee <= tranche.ValMax)
            {
                return amount;
            }
        }

        throw new InvalidOperationException(
            "Consumption exceeds the maximum configured tariff band.");
    }

    private async Task<Tarif> RequireTarifAsync(
        int tarifId,
        CancellationToken cancellationToken)
    {
        ValidateId(tarifId, nameof(tarifId));
        return await tarifDAO.GetByIdAsync(tarifId, cancellationToken)
            ?? throw new KeyNotFoundException("The tariff was not found.");
    }

    private async Task ValidateNoOverlapAsync(
        Tranche candidate,
        int? excludedId,
        CancellationToken cancellationToken)
    {
        List<Tranche> existing = await trancheDAO.GetByTarifIdAsync(
            candidate.IdTarif,
            cancellationToken);
        bool overlaps = existing.Any(tranche =>
            tranche.IdTranche != excludedId &&
            candidate.ValMin < tranche.ValMax &&
            tranche.ValMin < candidate.ValMax);
        if (overlaps)
        {
            throw new ArgumentException("Tariff bands for one tariff cannot overlap.");
        }
    }

    private static void ValidateRangeAndPrice(Tranche tranche)
    {
        if (tranche.ValMin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tranche.ValMin));
        }

        if (tranche.ValMax <= tranche.ValMin)
        {
            throw new ArgumentException("The maximum must be greater than the minimum.");
        }

        if (tranche.Prix <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tranche.Prix));
        }
    }

    private static void ValidateId(int id, string parameterName)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The ID must be positive.");
        }
    }
}
