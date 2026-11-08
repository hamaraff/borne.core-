using borne.core.Entities;
using System.Collections.Generic;

namespace borne.core.IInfra
{
    public interface IConfigDAO
    {
        Task<List<ConfigBorne>> GetAllConfigsBorneAsync(
            CancellationToken cancellationToken = default);

        Task<ConfigBorne?> GetConfigBorneByIdAsync(
            int idConfigBorne,
            CancellationToken cancellationToken = default);

        Task<ConfigBorne?> GetConfigBorneByCodeAsync(
            string codeBorne,
            CancellationToken cancellationToken = default);

        Task AddConfigBorneAsync(
            ConfigBorne configBorne,
            CancellationToken cancellationToken = default);

        Task UpdateConfigBorneAsync(
            ConfigBorne configBorne,
            CancellationToken cancellationToken = default);

        Task DeleteConfigBorneAsync(
            int idConfigBorne,
            CancellationToken cancellationToken = default);
    }
}
