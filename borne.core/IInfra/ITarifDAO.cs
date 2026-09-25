using borne.core.Entities;
using borne.core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.IInfra
{
    public interface ITarifDAO
    {
        Tarif GetById(int id);
        List<Tarif> getAll();
        List<Tarif> getByEnergie(TypeEnergie TypeEnergie);
        void Add(Tarif T);
        void update(Tarif T);
        void delete(Tarif T);
    }

}
