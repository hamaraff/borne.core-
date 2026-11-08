using borne.core.Enums;
using System;
using System.Collections.Generic;

namespace borne.core.Entities
{
    public class Reservation
    {
        public int IdReservation { get; set; }
        public int RefId { get; set; }
        public DateTime DateDeb { get; set; }
        public DateTime DateFin { get; set; }

        public TypeEnergie TypeEnergie { get; set; }
        public List<Consommation>? Consommations { get; set; }
        public List<Seuil>? Seuils { get; set; }

        public string? Tag { get; set; }

        public int IdBorne { get; set; }
    }
}
