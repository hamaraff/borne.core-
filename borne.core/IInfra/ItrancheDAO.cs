using borne.core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.IInfra
{
    public interface ITrancheDAO
    {
        List<Tranche> getAll();
        List<Tranche> getByTarifId(int tarifId);
        void add(Tranche tranche);
        void update(Tranche tranche);
        void delete(Tranche tranche);


    }
}
