using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;

namespace borne.business.Services;

public class ConsommationService
{
    private readonly IConsommationDAO consommationDAO;
    private readonly IReservationDAO reservationDAO;
    private readonly ITarifDAO tarifDAO;

    public ConsommationService(
        IConsommationDAO consommationDAO,
        IReservationDAO reservationDAO,
        ITarifDAO tarifDAO)
    {
        this.consommationDAO = consommationDAO;
        this.reservationDAO = reservationDAO;
        this.tarifDAO = tarifDAO;
    }

    public Task<Consommation?> GetConsommationByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id, nameof(id));
        return consommationDAO.GetByIdAsync(id, cancellationToken);
    }

    public Task<Consommation?> GetConsommationByReadingIdAsync(
        Guid readingId,
        CancellationToken cancellationToken = default)
    {
        if (readingId == Guid.Empty)
        {
            throw new ArgumentException("A reading ID is required.", nameof(readingId));
        }

        return consommationDAO.GetByReadingIdAsync(readingId, cancellationToken);
    }

    public Task<List<Consommation>> GetAllConsommationsAsync(
        CancellationToken cancellationToken = default)
    {
        return consommationDAO.GetAllAsync(cancellationToken);
    }

    public async Task<List<Consommation>> GetConsommationsByReservationAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequireReservationAsync(reservationId, cancellationToken);
        return await consommationDAO.GetByReservationIdAsync(reservationId, cancellationToken);
    }

    public Task<List<Consommation>> GetConsommationsByDateAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        if (date == default)
        {
            throw new ArgumentOutOfRangeException(nameof(date));
        }

        return consommationDAO.GetByDateAsync(date, cancellationToken);
    }

    public Task<List<Consommation>> GetConsommationsByDateRangeAsync(
        DateTime dateDebut,
        DateTime dateFin,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(dateDebut, dateFin);
        return consommationDAO.GetByDateRangeAsync(dateDebut, dateFin, cancellationToken);
    }

    public Task<Consommation?> GetCurrentConsommationByBorneIdAsync(
        int idBorne,
        CancellationToken cancellationToken = default)
    {
        ValidateId(idBorne, nameof(idBorne));
        return consommationDAO.GetLatestByBorneIdAsync(idBorne, cancellationToken);
    }

    public Task<List<Consommation>> GetConsommationsByBorneIdAsync(
        int idBorne,
        CancellationToken cancellationToken = default)
    {
        ValidateId(idBorne, nameof(idBorne));
        return consommationDAO.GetByBorneIdAsync(idBorne, cancellationToken);
    }

    public Task<List<Consommation>> GetConsommationsByBorneIdAndDateRangeAsync(
        int idBorne,
        DateTime dateDebut,
        DateTime dateFin,
        CancellationToken cancellationToken = default)
    {
        ValidateId(idBorne, nameof(idBorne));
        ValidateDateRange(dateDebut, dateFin);
        return consommationDAO.GetByBorneIdAndDateRangeAsync(
            idBorne,
            dateDebut,
            dateFin,
            cancellationToken);
    }

    public async Task<bool> AddConsommationAsync(
        Consommation consommation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consommation);
        if (consommation.IdConsommation != 0)
        {
            throw new ArgumentException("A new reading must not have a database ID.");
        }

        ValidateReading(consommation);
        Reservation reservation = await RequireReservationAsync(
            consommation.ReservationId,
            cancellationToken);
        Tarif tarif = await RequireTarifAsync(consommation.TarifId, cancellationToken);
        ValidateTarifForReservation(tarif, reservation);

        consommation.Tarif = tarif;
        consommation.Reservation = reservation;
        return await consommationDAO.AddAsync(consommation, cancellationToken);
    }

    public async Task UpdateConsommationAsync(
        Consommation consommation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consommation);
        ValidateId(consommation.IdConsommation, nameof(consommation.IdConsommation));
        Consommation? existing = await consommationDAO.GetByIdAsync(
            consommation.IdConsommation,
            cancellationToken);
        if (existing is null)
        {
            throw new KeyNotFoundException("The consumption reading was not found.");
        }

        if (existing.ReadingId != consommation.ReadingId)
        {
            throw new ArgumentException("A reading ID cannot be changed after creation.");
        }

        ValidateReading(consommation);
        Reservation reservation = await RequireReservationAsync(
            consommation.ReservationId,
            cancellationToken);
        Tarif tarif = await RequireTarifAsync(consommation.TarifId, cancellationToken);
        ValidateTarifForReservation(tarif, reservation);

        consommation.Tarif = tarif;
        consommation.Reservation = reservation;
        await consommationDAO.UpdateAsync(consommation, cancellationToken);
    }

    public async Task DeleteConsommationAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        _ = await GetConsommationByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("The consumption reading was not found.");
        await consommationDAO.DeleteAsync(id, cancellationToken);
    }

    private async Task<Reservation> RequireReservationAsync(
        int reservationId,
        CancellationToken cancellationToken)
    {
        ValidateId(reservationId, nameof(reservationId));
        return await reservationDAO.GetByIdAsync(reservationId, cancellationToken)
            ?? throw new KeyNotFoundException("The reservation was not found.");
    }

    private async Task<Tarif> RequireTarifAsync(
        int tarifId,
        CancellationToken cancellationToken)
    {
        ValidateId(tarifId, nameof(tarifId));
        Tarif tarif = await tarifDAO.GetByIdAsync(tarifId, cancellationToken)
            ?? throw new KeyNotFoundException("The tariff was not found.");
        if (!Enum.IsDefined(tarif.TypeEnergie))
        {
            throw new InvalidOperationException("The tariff has an unknown energy type.");
        }

        return tarif;
    }

    private static void ValidateReading(Consommation consommation)
    {
        if (consommation.Quantite <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consommation.Quantite),
                "The quantity must be positive.");
        }

        if (consommation.Montant < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consommation.Montant),
                "The amount cannot be negative.");
        }

        if (consommation.Date == default)
        {
            throw new ArgumentOutOfRangeException(nameof(consommation.Date));
        }

        if (consommation.ReadingId == Guid.Empty)
        {
            throw new ArgumentException("A reading ID is required.", nameof(consommation));
        }

        ValidateId(consommation.ReservationId, nameof(consommation.ReservationId));
        ValidateId(consommation.TarifId, nameof(consommation.TarifId));
    }

    private static void ValidateTarifForReservation(Tarif tarif, Reservation reservation)
    {
        if (tarif.TypeEnergie != reservation.TypeEnergie)
        {
            throw new ArgumentException("The tariff energy type must match the reservation.");
        }
    }

    private static void ValidateDateRange(DateTime dateDebut, DateTime dateFin)
    {
        if (dateDebut == default || dateFin == default || dateDebut >= dateFin)
        {
            throw new ArgumentException(
                "The date range must have valid endpoints and start before its end.");
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
