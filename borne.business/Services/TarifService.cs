using borne.core.IInfra;
using borne.core.Entities;
using borne.core.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace borne.business.Services
{
    public class TarifService
    {
        private readonly ITarifDAO tarifDAO; 
        public TarifService(ITarifDAO tarifDAO)
        {
            this.tarifDAO = tarifDAO;
        }

        public async Task<List<Tarif>> GetAllTarifsAsync()
        {
            return await tarifDAO.GetAllAsync();
        }

        public async Task<List<Tarif>> GetTarifsByEnergieAsync(TypeEnergie typeEnergie)
        {
            return await tarifDAO.GetByEnergieAsync(typeEnergie);
        }

        public async Task AddTarifAsync(Tarif tarif)
        {
            if (tarif == null)
            {
                throw new ArgumentNullException(nameof(tarif));
            }


            if (!Enum.IsDefined(typeof(TypeEnergie), tarif.TypeEnergie))
            {
                throw new ArgumentException(
                    "Le type d'énergie spécifié est invalide.",
                    nameof(tarif.TypeEnergie)
                );
            }

            await tarifDAO.AddAsync(tarif);
        }

        public async Task UpdateTarifAsync(Tarif tarif)
        {
            if (tarif == null)
            {
                throw new ArgumentNullException(nameof(tarif));
            }

            Tarif existingTarif = await tarifDAO.GetByIdAsync(tarif.IdTarif);

            if (existingTarif == null)
            {
                throw new ArgumentException("Le tarif n'existe pas.");
            }



            if (!Enum.IsDefined(typeof(TypeEnergie), tarif.TypeEnergie))
            {
                throw new ArgumentException(
                    "Le type d'énergie spécifié est invalide.",
                    nameof(tarif.TypeEnergie)
                );
            }

            await tarifDAO.UpdateAsync(tarif);
        }

        public async Task DeleteTarifAsync(int idTarif)
        {
            Tarif existingTarif = await tarifDAO.GetByIdAsync(idTarif);
            if (existingTarif == null)
            {
                throw new ArgumentException("Le tarif n'existe pas.");
            }
            await tarifDAO.DeleteAsync(existingTarif);
        }



    }
}
