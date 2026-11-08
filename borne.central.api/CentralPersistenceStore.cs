using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using borne.infrastructure.Data;

namespace borne.central.api;

public sealed class CentralPersistenceStore(MySqlConnectionFactory connections)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = connections.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("SELECT COUNT(*) FROM borne_runtime_status", connection);
            _ = await command.ExecuteScalarAsync(cancellationToken);
            await using var eventTableCheck = new MySqlCommand("SELECT COUNT(*) FROM borne_event", connection);
            _ = await eventTableCheck.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (MySqlException) { return false; }
    }

    public async Task RecordHeartbeatAsync(string borneId, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        const string sql = "INSERT INTO borne_runtime_status (borne_id, last_heartbeat_utc) VALUES (@borneId, @received) " +
                           "ON DUPLICATE KEY UPDATE last_heartbeat_utc = GREATEST(last_heartbeat_utc, VALUES(last_heartbeat_utc))";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@borneId", borneId);
        command.Parameters.AddWithValue("@received", receivedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredEventResult> PersistEventAsync(
        Guid eventId, string borneId, string eventType, string payloadJson,
        DateTimeOffset occurredAtUtc, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("An event ID is required.", nameof(eventId));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string insertSql = "INSERT INTO borne_event (event_id, borne_id, event_type, payload_json, payload_sha256, occurred_at_utc, received_at_utc) " +
                                     "VALUES (@id, @borneId, @eventType, CAST(@payload AS JSON), @hash, @occurred, @received) " +
                                     "ON DUPLICATE KEY UPDATE event_id = event_id";
            await using var insert = new MySqlCommand(insertSql, connection, transaction);
            insert.Parameters.AddWithValue("@id", eventId.ToString("D"));
            insert.Parameters.AddWithValue("@borneId", borneId);
            insert.Parameters.AddWithValue("@eventType", eventType);
            insert.Parameters.AddWithValue("@payload", payloadJson);
            insert.Parameters.Add("@hash", MySqlDbType.Binary, 32).Value = hash;
            insert.Parameters.AddWithValue("@occurred", occurredAtUtc.UtcDateTime);
            insert.Parameters.AddWithValue("@received", receivedAtUtc.UtcDateTime);
            int inserted = await insert.ExecuteNonQueryAsync(cancellationToken);

            const string selectSql = "SELECT borne_id, event_type, payload_sha256 FROM borne_event WHERE event_id = @id FOR UPDATE";
            await using var select = new MySqlCommand(selectSql, connection, transaction);
            select.Parameters.AddWithValue("@id", eventId.ToString("D"));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Persisted event could not be read back.");
            bool same = string.Equals(reader.GetString("borne_id"), borneId, StringComparison.Ordinal) &&
                        string.Equals(reader.GetString("event_type"), eventType, StringComparison.Ordinal) &&
                        CryptographicOperations.FixedTimeEquals((byte[])reader["payload_sha256"], hash);
            await reader.DisposeAsync();
            if (!same) throw new InvalidOperationException("An event ID was reused with a different payload.");
            await transaction.CommitAsync(cancellationToken);
            return new StoredEventResult(inserted > 0);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<StoredEvent>> GetEventsAsync(string borneId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT event_id, event_type, payload_json, occurred_at_utc, received_at_utc FROM borne_event WHERE borne_id = @id ORDER BY received_at_utc DESC LIMIT @limit", connection);
        command.Parameters.AddWithValue("@id", borneId);
        command.Parameters.AddWithValue("@limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredEvent>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new StoredEvent(ReadGuid(reader, "event_id"), borneId, reader.GetString("event_type"), reader.GetString("payload_json"), DateTime.SpecifyKind(reader.GetDateTime("occurred_at_utc"), DateTimeKind.Utc), DateTime.SpecifyKind(reader.GetDateTime("received_at_utc"), DateTimeKind.Utc)));
        return rows;
    }

    public async Task<IReadOnlyList<StoredEvent>> GetRecentEventsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT event_id, borne_id, event_type, payload_json, occurred_at_utc, received_at_utc " +
                           "FROM (SELECT event_id, borne_id, event_type, payload_json, occurred_at_utc, received_at_utc " +
                           "FROM borne_event ORDER BY received_at_utc DESC LIMIT @limit) recent ORDER BY received_at_utc";
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<StoredEvent>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new StoredEvent(ReadGuid(reader, "event_id"), reader.GetString("borne_id"), reader.GetString("event_type"), reader.GetString("payload_json"), DateTime.SpecifyKind(reader.GetDateTime("occurred_at_utc"), DateTimeKind.Utc), DateTime.SpecifyKind(reader.GetDateTime("received_at_utc"), DateTimeKind.Utc)));
        return rows;
    }

    public async Task<IReadOnlyList<BorneHealth>> GetAllHealthAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT borne_id, last_heartbeat_utc FROM borne_runtime_status", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<BorneHealth>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var last = DateTime.SpecifyKind(reader.GetDateTime("last_heartbeat_utc"), DateTimeKind.Utc);
            var lastAt = new DateTimeOffset(last);
            rows.Add(new BorneHealth(reader.GetString("borne_id"), lastAt, nowUtc - lastAt < TimeSpan.FromSeconds(35)));
        }
        return rows;
    }

    public async Task<BorneHealth?> GetHealthAsync(string borneId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT last_heartbeat_utc FROM borne_runtime_status WHERE borne_id = @id", connection);
        command.Parameters.AddWithValue("@id", borneId);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not DateTime last) return null;
        DateTimeOffset received = new(DateTime.SpecifyKind(last, DateTimeKind.Utc));
        return new BorneHealth(borneId, received, nowUtc - received < TimeSpan.FromSeconds(35));
    }

    private static Guid ReadGuid(MySqlDataReader reader, string column)
    {
        object value = reader.GetValue(reader.GetOrdinal(column));
        return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!);
    }
}

public sealed record StoredEventResult(bool Accepted);
public sealed record StoredEvent(Guid EventId, string BorneId, string EventType, string PayloadJson, DateTime OccurredAtUtc, DateTime ReceivedAtUtc);
public sealed record BorneHealth(string BorneId, DateTimeOffset LastHeartbeatUtc, bool IsOnline);
