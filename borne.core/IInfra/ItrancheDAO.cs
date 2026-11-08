using borne.core.Entities;
using System.Collections.Generic;

namespace borne.core.IInfra
{
    public interface ITrancheDAO
    {
        Task<Tranche?> GetByIdAsync(int idTranche, CancellationToken cancellationToken = default);
        Task<List<Tranche>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<List<Tranche>> GetByTarifIdAsync(
            int tarifId,
            CancellationToken cancellationToken = default);

        Task AddAsync(Tranche tranche, CancellationToken cancellationToken = default);
        Task UpdateAsync(Tranche tranche, CancellationToken cancellationToken = default);
        Task DeleteAsync(Tranche tranche, CancellationToken cancellationToken = default);
    }
}
