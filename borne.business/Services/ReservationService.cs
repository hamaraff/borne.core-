using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;

namespace borne.business.Services;

public class ReservationService
{
    private readonly IReservationDAO reservationDAO;

    public ReservationService(IReservationDAO reservationDAO)
    {
        this.reservationDAO = reservationDAO;
    }

    public Task<Reservation?> GetReservationByIdAsync(
        int idReservation,
        CancellationToken cancellationToken = default)
    {
        ValidateId(idReservation, nameof(idReservation));
        return reservationDAO.GetByIdAsync(idReservation, cancellationToken);
    }

    public Task<List<Reservation>> GetAllReservationsAsync(
        CancellationToken cancellationToken = default)
    {
        return reservationDAO.GetAllAsync(cancellationToken);
    }

    public async Task AddReservationAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.IdReservation != 0)
        {
            throw new ArgumentException("A new reservation must not have a database ID.");
        }

        ValidateReservation(reservation);
        await reservationDAO.AddAsync(reservation, cancellationToken);
    }

    public async Task UpdateReservationAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateId(reservation.IdReservation, nameof(reservation.IdReservation));
        ValidateReservation(reservation);

        if (await reservationDAO.GetByIdAsync(
                reservation.IdReservation,
                cancellationToken) is null)
        {
            throw new KeyNotFoundException("The reservation was not found.");
        }

        await reservationDAO.UpdateAsync(reservation, cancellationToken);
    }

    public async Task DeleteReservationAsync(
        int idReservation,
        CancellationToken cancellationToken = default)
    {
        ValidateId(idReservation, nameof(idReservation));
        if (await reservationDAO.GetByIdAsync(idReservation, cancellationToken) is null)
        {
            throw new KeyNotFoundException("The reservation was not found.");
        }

        await reservationDAO.DeleteAsync(idReservation, cancellationToken);
    }

    private static void ValidateReservation(Reservation reservation)
    {
        if (reservation.DateDeb >= reservation.DateFin)
        {
            throw new ArgumentException("The reservation start must precede its end.");
        }

        ValidateId(reservation.IdBorne, nameof(reservation.IdBorne));
        if (!Enum.IsDefined(reservation.TypeEnergie))
        {
            throw new ArgumentOutOfRangeException(nameof(reservation.TypeEnergie));
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
