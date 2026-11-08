using borne.core.Entities;
using borne.core.Enums;
using System.Collections.Generic;

namespace borne.core.IInfra
{
    public interface ITarifDAO
    {
        Task<Tarif?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<List<Tarif>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<List<Tarif>> GetByEnergieAsync(
            TypeEnergie typeEnergie,
            CancellationToken cancellationToken = default);

        Task AddAsync(Tarif tarif, CancellationToken cancellationToken = default);
        Task UpdateAsync(Tarif tarif, CancellationToken cancellationToken = default);
        Task DeleteAsync(Tarif tarif, CancellationToken cancellationToken = default);
    }

}
