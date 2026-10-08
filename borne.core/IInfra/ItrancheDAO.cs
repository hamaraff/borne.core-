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
        List<Tranche> GetAll();
        List<Tranche> GetByTarifId(int tarifId);

        void Add(Tranche tranche);
        void Update(Tranche tranche);
        void Delete(Tranche tranche);
    }
}
