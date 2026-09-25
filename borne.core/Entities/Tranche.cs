using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.Entities
{
    public class Tranche
    {
        public int valeurDebut {  get; set; }
        public int valeurFin {  get ; set; }
        public double prix {  get; set; }
        public Tarif Tarif { get; set; }
    }
}
