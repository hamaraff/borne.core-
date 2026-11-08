using borne.core.Enums;

namespace borne.core.Entities
{
    public class Seuil
    {
        public int IdSeuil { get; set; }
        public decimal Valeur { get; set; }
        public int ReservationId { get; set; }
        public Reservation? Reservation { get; set; }

        public TypeSeuil TypeSeuil { get; set; }
    }
}
