using borne.core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace borne.core.IInfra
{
    public interface IConfigDAO
    {
        List<ConfigBorne> GetAllConfigsBorne();

        ConfigBorne GetConfigBorneById(int idConfigBorne);

        void AddConfigBorne(ConfigBorne configBorne);

        void UpdateConfigBorne(ConfigBorne configBorne);

        void DeleteConfigBorne(int idConfigBorne);
    }
}
