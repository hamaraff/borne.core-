using borne.core.Entities;
using System.Collections.Generic;

namespace borne.core.IInfra
{
    public interface ISeuilDAO
    {
        Task<Seuil?> GetByIdAsync(int idSeuil, CancellationToken cancellationToken = default);
        Task<List<Seuil>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<List<Seuil>> GetByReservationIdAsync(
            int reservationId,
            CancellationToken cancellationToken = default);
        Task AddAsync(Seuil seuil, CancellationToken cancellationToken = default);
        Task UpdateAsync(Seuil seuil, CancellationToken cancellationToken = default);
        Task DeleteAsync(int idSeuil, CancellationToken cancellationToken = default);
    }
}
