using borne.core.Entities;
using System.Collections.Generic;

namespace borne.core.IInfra
{
    public interface IReservationDAO
    {
        Task<Reservation?> GetByIdAsync(int idReservation, CancellationToken cancellationToken = default);
        Task<List<Reservation>> GetAllAsync(CancellationToken cancellationToken = default);
        Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default);
        Task UpdateAsync(Reservation reservation, CancellationToken cancellationToken = default);
        Task DeleteAsync(int idReservation, CancellationToken cancellationToken = default);
    }
}
