using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;

namespace borne.business.Services;

public class SeuilService
{
    private readonly ISeuilDAO seuilDAO;
    private readonly IReservationDAO reservationDAO;

    public SeuilService(ISeuilDAO seuilDAO, IReservationDAO reservationDAO)
    {
        this.seuilDAO = seuilDAO;
        this.reservationDAO = reservationDAO;
    }

    public async Task AddSeuilAsync(
        Seuil seuil,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seuil);
        if (seuil.IdSeuil != 0)
        {
            throw new ArgumentException("A new threshold must not have a database ID.");
        }

        ValidateValue(seuil.Valeur);
        ValidateType(seuil.TypeSeuil);
        Reservation reservation = await RequireReservationAsync(
            seuil.ReservationId,
            cancellationToken);
        seuil.Reservation = reservation;
        await seuilDAO.AddAsync(seuil, cancellationToken);
    }

    public async Task UpdateSeuilAsync(
        Seuil seuil,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seuil);
        ValidateId(seuil.IdSeuil, nameof(seuil.IdSeuil));
        if (await seuilDAO.GetByIdAsync(seuil.IdSeuil, cancellationToken) is null)
        {
            throw new KeyNotFoundException("The threshold was not found.");
        }

        ValidateValue(seuil.Valeur);
        ValidateType(seuil.TypeSeuil);
        seuil.Reservation = await RequireReservationAsync(
            seuil.ReservationId,
            cancellationToken);
        await seuilDAO.UpdateAsync(seuil, cancellationToken);
    }

    public async Task DeleteSeuilAsync(
        int idSeuil,
        CancellationToken cancellationToken = default)
    {
        _ = await GetSeuilByIdAsync(idSeuil, cancellationToken)
            ?? throw new KeyNotFoundException("The threshold was not found.");
        await seuilDAO.DeleteAsync(idSeuil, cancellationToken);
    }

    public async Task<Seuil?> GetSeuilByIdAsync(
        int idSeuil,
        CancellationToken cancellationToken = default)
    {
        ValidateId(idSeuil, nameof(idSeuil));
        return await seuilDAO.GetByIdAsync(idSeuil, cancellationToken);
    }

    public Task<List<Seuil>> GetAllSeuilsAsync(
        CancellationToken cancellationToken = default)
    {
        return seuilDAO.GetAllAsync(cancellationToken);
    }

    public async Task<List<Seuil>> GetSeuilsByReservationAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
    {
        _ = await RequireReservationAsync(reservationId, cancellationToken);
        return await seuilDAO.GetByReservationIdAsync(reservationId, cancellationToken);
    }

    private async Task<Reservation> RequireReservationAsync(
        int reservationId,
        CancellationToken cancellationToken)
    {
        ValidateId(reservationId, nameof(reservationId));
        return await reservationDAO.GetByIdAsync(reservationId, cancellationToken)
            ?? throw new KeyNotFoundException("The reservation was not found.");
    }

    private static void ValidateValue(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The threshold value must be positive.");
        }
    }

    private static void ValidateType(TypeSeuil typeSeuil)
    {
        if (!Enum.IsDefined(typeSeuil))
        {
            throw new ArgumentOutOfRangeException(nameof(typeSeuil));
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
