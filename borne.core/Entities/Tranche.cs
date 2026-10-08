using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.Entities
{
    public class Tranche
    {
        public int IdTranche { get; set; }
        public double ValMin {  get; set; }
        public double ValMax {  get ; set; }
        public double Prix {  get; set; }
        public int IdTarif { get; set; }
    }
}
