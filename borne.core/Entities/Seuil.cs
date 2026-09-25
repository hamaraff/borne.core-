using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.Entities
{
    public class Seuil
    {
        public int IdSeuil { get; set; }
        public decimal Valeur { get; set; }
        public int ReservationId { get; set; }
        public Reservation Reservation { get; set; }
    }
}
