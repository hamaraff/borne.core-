using borne.core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.Entities
{
    public class Tarif
    {
        public int IdTarif { get; set; }
        public decimal Valeur { get; set; }
        public TypeEnergie TypeEnergie { get; set; }
    }
}
