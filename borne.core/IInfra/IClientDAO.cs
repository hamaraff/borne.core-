using borne.core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.IInfra
{
    public interface IClientDAO
    {
        Client GetById(int idClient);

        List<Client> GetAll();

        void Add(Client client);

        void Update(Client client);

        void Delete(int idClient);
    }
}
