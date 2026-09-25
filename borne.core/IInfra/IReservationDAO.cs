using borne.core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.IInfra
{
    public interface IReservationDAO
    {
        Reservation GetById(int idReservation);
        List<Reservation> GetAll();
        List<Reservation> GetByClientId(int idClient);
        void Add(Reservation reservation);
        void Update(Reservation reservation);
        void Delete(int idReservation);
    }
}
