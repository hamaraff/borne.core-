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
        List<Tarif> GetAll();
        List<Tarif> GetByEnergie(TypeEnergie typeEnergie);

        void Add(Tarif tarif);
        void Update(Tarif tarif);
        void Delete(Tarif tarif);
    }

}
