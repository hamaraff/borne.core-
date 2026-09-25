using borne.core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.IInfra
{
    public interface ISeuilDAO
    {
        Seuil GetById(int idSeuil);
        List<Seuil> GetAll();
        List<Seuil> GetByReservationId(int reservationId);
        void Add(Seuil seuil);
        void Update(Seuil seuil);
        void Delete(int idSeuil);
    }
}
