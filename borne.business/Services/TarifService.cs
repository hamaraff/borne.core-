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

        public List<Tarif> GetAllTarifs()
        {
            return tarifDAO.GetAll();
        }

        public List<Tarif> GetTarifsByEnergie(TypeEnergie typeEnergie)
        {
            return tarifDAO.GetByEnergie(typeEnergie);
        }

        public void AddTarif(Tarif tarif)
        {
            if (tarif == null)
            {
                throw new ArgumentNullException(nameof(tarif));
            }

            if (tarif.Valeur <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tarif.Valeur),
                    "La valeur du tarif doit être supérieure à 0."
                );
            }

            if (!Enum.IsDefined(typeof(TypeEnergie), tarif.TypeEnergie))
            {
                throw new ArgumentException(
                    "Le type d'énergie spécifié est invalide.",
                    nameof(tarif.TypeEnergie)
                );
            }

            tarifDAO.Add(tarif);
        }

        public void UpdateTarif(Tarif tarif)
        {
            if (tarif == null)
            {
                throw new ArgumentNullException(nameof(tarif));
            }

            Tarif existingTarif = tarifDAO.GetById(tarif.IdTarif);

            if (existingTarif == null)
            {
                throw new ArgumentException("Le tarif n'existe pas.");
            }

            if (tarif.Valeur <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tarif.Valeur),
                    "La valeur du tarif doit être supérieure à 0."
                );
            }

            if (!Enum.IsDefined(typeof(TypeEnergie), tarif.TypeEnergie))
            {
                throw new ArgumentException(
                    "Le type d'énergie spécifié est invalide.",
                    nameof(tarif.TypeEnergie)
                );
            }

            tarifDAO.Update(tarif);
        }

        public void DeleteTarif(int idTarif)
        {
            Tarif existingTarif = tarifDAO.GetById(idTarif);
            if (existingTarif == null)
            {
                throw new ArgumentException("Le tarif n'existe pas.");
            }
            tarifDAO.Delete(existingTarif);
        }



    }
}
