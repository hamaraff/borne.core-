using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.Entities
{
    public class Tranche
    {
        public double valMin {  get; set; }
        public double valMax {  get ; set; }
        public double prix {  get; set; }
        public Tarif Tarif { get; set; }
    }
}
