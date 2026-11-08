using borne.core.Entities;
using borne.core.IInfra;
using borne.infrastructure.Data;
using MySqlConnector;

namespace borne.infrastructure.Daos;

public sealed class MySqlConfigBorneDAO : IConfigDAO
{
    private readonly MySqlConnectionFactory connectionFactory;

    public MySqlConfigBorneDAO(MySqlConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<List<ConfigBorne>> GetAllConfigsBorneAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "SELECT id_borne, code_borne FROM borne ORDER BY id_borne",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var bornes = new List<ConfigBorne>();
        while (await reader.ReadAsync(cancellationToken))
        {
            bornes.Add(Map(reader));
        }

        return bornes;
    }

    public async Task<ConfigBorne?> GetConfigBorneByIdAsync(
        int idConfigBorne,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "SELECT id_borne, code_borne FROM borne WHERE id_borne = @id",
            connection
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, idConfigBorne);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<ConfigBorne?> GetConfigBorneByCodeAsync(
        string codeBorne,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeBorne);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "SELECT id_borne, code_borne FROM borne WHERE code_borne = @code",
            connection
        );
        MySqlData.AddParameter(command, "@code", MySqlDbType.VarChar, codeBorne);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task AddConfigBorneAsync(
        ConfigBorne configBorne,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configBorne);
        ArgumentException.ThrowIfNullOrWhiteSpace(configBorne.codeBorne);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "INSERT INTO borne (code_borne) VALUES (@code)",
            connection
        );
        MySqlData.AddParameter(
            command,
            "@code",
            MySqlDbType.VarChar,
            configBorne.codeBorne
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
        configBorne.idConfigBorne = checked((int)command.LastInsertedId);
    }

    public async Task UpdateConfigBorneAsync(
        ConfigBorne configBorne,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configBorne);
        ArgumentException.ThrowIfNullOrWhiteSpace(configBorne.codeBorne);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "UPDATE borne SET code_borne = @code WHERE id_borne = @id",
            connection
        );
        MySqlData.AddParameter(
            command,
            "@code",
            MySqlDbType.VarChar,
            configBorne.codeBorne
        );
        MySqlData.AddParameter(
            command,
            "@id",
            MySqlDbType.Int32,
            configBorne.idConfigBorne
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteConfigBorneAsync(
        int idConfigBorne,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "DELETE FROM borne WHERE id_borne = @id",
            connection
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, idConfigBorne);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ConfigBorne Map(MySqlDataReader reader)
    {
        return new ConfigBorne
        {
            idConfigBorne = reader.GetInt32("id_borne"),
            codeBorne = reader.GetString("code_borne")
        };
    }
}
