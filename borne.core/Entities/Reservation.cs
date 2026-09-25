using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.Entities
{
    public class Reservation
    {
        public int IdReservation {  get; set; }
        public int IdClient {  get; set; }
        public int RefId { get; set; }
        public DateTime DateDeb { get; set; }
        public DateTime DateFin { get; set; }
        public List<Consommation> Consommations { get; set; }
        public List<Seuil> Seuils { get; set; }
    }
}
