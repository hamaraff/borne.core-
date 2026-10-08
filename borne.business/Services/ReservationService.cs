using borne.core.Entities;
using borne.core.IInfra;
using System.Linq;


namespace borne.business.Services
{
    public class ReservationService
    {
        private readonly IReservationDAO reservationDAO;
        private readonly IClientDAO clientDAO;

        public ReservationService(
            IReservationDAO reservationDAO,
            IClientDAO clientDAO)
        {
            this.reservationDAO = reservationDAO;
            this.clientDAO = clientDAO;
        }

        private bool HasConflict(Reservation reservation)
        {
            List<Reservation> reservations =
                reservationDAO.GetByClientId(reservation.IdClient);

            return reservations.Any(r =>
                r.IdReservation != reservation.IdReservation &&
                r.DateDeb < reservation.DateFin &&
                r.DateFin > reservation.DateDeb
            );
        }

        public void AddReservation(Reservation reservation)
        {
            if(reservation == null)
            {
                throw new ArgumentNullException(nameof(reservation));
            }

            Client client = clientDAO.GetById(reservation.IdClient);
            if(client == null)
            {
                throw new ArgumentException(
                    "Le client spécifié n'existe pas.",
                    nameof(reservation.IdClient)
);
            }

            if (reservation.DateDeb >= reservation.DateFin)
            {
                throw new ArgumentException("Date Debut doit être avant Date Fin");
            }
            if (HasConflict(reservation))
            {
                throw new ArgumentException(
                    "Le client possède déjà une réservation durant cette période."
                );
            }

            reservationDAO.Add(reservation);
        }

        public void UpdateReservation(Reservation reservation)
        {
            if (reservation == null)
            {
                throw new ArgumentNullException(nameof(reservation));
            }

            Reservation existingReservation =
                reservationDAO.GetById(reservation.IdReservation);

            if (existingReservation == null)
            {
                throw new ArgumentException(
                    "La réservation spécifiée n'existe pas.",
                    nameof(reservation.IdReservation)
                );
            }

            Client client = clientDAO.GetById(reservation.IdClient);

            if (client == null)
            {
                throw new ArgumentException(
                    "Le client spécifié n'existe pas.",
                    nameof(reservation.IdClient)
                );
            }

            if (reservation.DateDeb >= reservation.DateFin)
            {
                throw new ArgumentException(
                    "La date de début doit être antérieure à la date de fin."
                );
            }

            if (HasConflict(reservation))
            {
                throw new ArgumentException(
                    "Le client possède déjà une réservation durant cette période."
                );
            }

            reservationDAO.Update(reservation);
        }

        public void DeleteReservation(int idReservation)
        {
            Reservation reservation = reservationDAO.GetById(idReservation);

            if (reservation == null)
            {
                throw new ArgumentException(
                    "La réservation spécifiée n'existe pas.",
                    nameof(idReservation)
                );
            }

            reservationDAO.Delete(idReservation);
        }
    }
}