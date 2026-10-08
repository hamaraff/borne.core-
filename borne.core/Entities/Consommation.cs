using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.Entities
{
    public class Consommation
    {
        public int IdConsommation { get; set; }
        public DateTime Date { get; set; }
        public int Quantite {  get; set; }

        public double Montant { get; set; }


        public int TarifId { get; set; }
        public Tarif Tarif { get; set; }

        public int ReservationId { get; set; }
        public Reservation Reservation { get; set; }
    }
}
