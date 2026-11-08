using borne.core.Entities;
using System;
using System.Collections.Generic;

namespace borne.core.IInfra
{
    public interface IConsommationDAO
    {
        Task<Consommation?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<Consommation?> GetByReadingIdAsync(Guid readingId, CancellationToken cancellationToken = default);
        Task<List<Consommation>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<List<Consommation>> GetByReservationIdAsync(
            int reservationId,
            CancellationToken cancellationToken = default);
        Task<List<Consommation>> GetByBorneIdAsync(
            int idBorne,
            CancellationToken cancellationToken = default);
        Task<List<Consommation>> GetByBorneIdAndDateRangeAsync(
            int idBorne,
            DateTime dateDebut,
            DateTime dateFin,
            CancellationToken cancellationToken = default);
        Task<Consommation?> GetLatestByBorneIdAsync(
            int idBorne,
            CancellationToken cancellationToken = default);

        Task<List<Consommation>> GetByDateAsync(
            DateTime date,
            CancellationToken cancellationToken = default);
        Task<List<Consommation>> GetByDateRangeAsync(
            DateTime dateDebut,
            DateTime dateFin,
            CancellationToken cancellationToken = default);
        Task<bool> AddAsync(Consommation consommation, CancellationToken cancellationToken = default);
        Task UpdateAsync(Consommation consommation, CancellationToken cancellationToken = default);
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
