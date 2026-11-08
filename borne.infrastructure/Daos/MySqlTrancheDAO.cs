using borne.core.Entities;
using borne.core.IInfra;
using borne.infrastructure.Data;
using MySqlConnector;

namespace borne.infrastructure.Daos;

public sealed class MySqlTrancheDAO : ITrancheDAO
{
    private const string SelectColumns = "id_tranche, val_min, val_max, prix, id_tarif";

    private readonly MySqlConnectionFactory connectionFactory;

    public MySqlTrancheDAO(MySqlConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<Tranche?> GetByIdAsync(
        int idTranche,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM tranche WHERE id_tranche = @id",
            connection);
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, idTranche);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<List<Tranche>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM tranche ORDER BY id_tarif, val_min, id_tranche",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tranches = new List<Tranche>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tranches.Add(Map(reader));
        }

        return tranches;
    }

    public async Task<List<Tranche>> GetByTarifIdAsync(
        int tarifId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM tranche WHERE id_tarif = @tarifId " +
            "ORDER BY val_min, id_tranche",
            connection);
        MySqlData.AddParameter(command, "@tarifId", MySqlDbType.Int32, tarifId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tranches = new List<Tranche>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tranches.Add(Map(reader));
        }

        return tranches;
    }

    public async Task AddAsync(
        Tranche tranche,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tranche);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "INSERT INTO tranche (val_min, val_max, prix, id_tarif) " +
            "VALUES (@minimum, @maximum, @price, @tarifId)",
            connection);
        AddParameters(command, tranche);
        await command.ExecuteNonQueryAsync(cancellationToken);
        tranche.IdTranche = checked((int)command.LastInsertedId);
    }

    public async Task UpdateAsync(
        Tranche tranche,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tranche);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "UPDATE tranche SET val_min = @minimum, val_max = @maximum, " +
            "prix = @price, id_tarif = @tarifId WHERE id_tranche = @id",
            connection);
        AddParameters(command, tranche);
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, tranche.IdTranche);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Tranche tranche,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tranche);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "DELETE FROM tranche WHERE id_tranche = @id",
            connection);
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, tranche.IdTranche);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Tranche Map(MySqlDataReader reader)
    {
        return new Tranche
        {
            IdTranche = reader.GetInt32("id_tranche"),
            ValMin = reader.GetDecimal("val_min"),
            ValMax = reader.GetDecimal("val_max"),
            Prix = reader.GetDecimal("prix"),
            IdTarif = reader.GetInt32("id_tarif")
        };
    }

    private static void AddParameters(MySqlCommand command, Tranche tranche)
    {
        MySqlData.AddParameter(command, "@minimum", MySqlDbType.Decimal, tranche.ValMin);
        MySqlData.AddParameter(command, "@maximum", MySqlDbType.Decimal, tranche.ValMax);
        MySqlData.AddParameter(command, "@price", MySqlDbType.Decimal, tranche.Prix);
        MySqlData.AddParameter(command, "@tarifId", MySqlDbType.Int32, tranche.IdTarif);
    }
}
