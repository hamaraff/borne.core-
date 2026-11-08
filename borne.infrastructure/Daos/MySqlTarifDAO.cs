using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;
using borne.infrastructure.Data;
using MySqlConnector;

namespace borne.infrastructure.Daos;

public sealed class MySqlTarifDAO : ITarifDAO
{
    private const string SelectColumns = "id_tarif, type_energie";

    private readonly MySqlConnectionFactory connectionFactory;

    public MySqlTarifDAO(MySqlConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<Tarif?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM tarif WHERE id_tarif = @id",
            connection
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<List<Tarif>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM tarif ORDER BY id_tarif",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tariffs = new List<Tarif>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tariffs.Add(Map(reader));
        }

        return tariffs;
    }

    public async Task<List<Tarif>> GetByEnergieAsync(
        TypeEnergie typeEnergie,
        CancellationToken cancellationToken = default)
    {
        ValidateEnergyType(typeEnergie);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM tarif WHERE type_energie = @type " +
            "ORDER BY id_tarif",
            connection
        );
        MySqlData.AddParameter(
            command,
            "@type",
            MySqlDbType.VarChar,
            typeEnergie.ToString()
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tariffs = new List<Tarif>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tariffs.Add(Map(reader));
        }

        return tariffs;
    }

    public async Task AddAsync(
        Tarif tarif,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tarif);
        ValidateEnergyType(tarif.TypeEnergie);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "INSERT INTO tarif (type_energie) VALUES (@type)",
            connection
        );
        MySqlData.AddParameter(
            command,
            "@type",
            MySqlDbType.VarChar,
            tarif.TypeEnergie.ToString()
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
        tarif.IdTarif = checked((int)command.LastInsertedId);
    }

    public async Task UpdateAsync(
        Tarif tarif,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tarif);
        ValidateEnergyType(tarif.TypeEnergie);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "UPDATE tarif SET type_energie = @type WHERE id_tarif = @id",
            connection
        );
        MySqlData.AddParameter(
            command,
            "@type",
            MySqlDbType.VarChar,
            tarif.TypeEnergie.ToString()
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, tarif.IdTarif);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Tarif tarif,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tarif);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "DELETE FROM tarif WHERE id_tarif = @id",
            connection
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, tarif.IdTarif);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Tarif Map(MySqlDataReader reader)
    {
        return new Tarif
        {
            IdTarif = reader.GetInt32("id_tarif"),
            TypeEnergie = MySqlData.ReadEnum<TypeEnergie>(reader, "type_energie")
        };
    }

    private static void ValidateEnergyType(TypeEnergie typeEnergie)
    {
        if (!Enum.IsDefined(typeEnergie))
        {
            throw new ArgumentOutOfRangeException(nameof(typeEnergie));
        }
    }
}
