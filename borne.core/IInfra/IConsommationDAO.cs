using borne.core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.IInfra
{
    public interface IConsommationDAO
    {
        Consommation GetById(int id);
        List<Consommation> GetAll();
        List<Consommation> GetByReservationId(int reservationId);

        List<Consommation> GetByDate(DateTime date);
        List<Consommation> GetByDateRange(DateTime DD, DateTime DF);
        void Add(Consommation consommation);
        void Update(Consommation consommation);
        void Delete(int id);
    }
}
