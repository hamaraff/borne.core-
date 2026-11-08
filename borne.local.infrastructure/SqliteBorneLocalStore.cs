using System.Globalization;
using System.Text.Json;
using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;
using Microsoft.Data.Sqlite;

namespace borne.local.infrastructure;

public sealed class SqliteBorneLocalStore : IBorneLocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public SqliteBorneLocalStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            CREATE TABLE IF NOT EXISTS cached_reservation (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                reservation_id INTEGER NOT NULL UNIQUE,
                ref_id INTEGER NOT NULL,
                starts_at_utc TEXT NOT NULL,
                ends_at_utc TEXT NOT NULL,
                energy_type TEXT NOT NULL,
                card_tag_hash TEXT NOT NULL,
                borne_id INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tariff_snapshot (
                energy_type TEXT NOT NULL,
                version TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                cached_at_utc TEXT NOT NULL,
                PRIMARY KEY (energy_type, version)
            );
            CREATE TABLE IF NOT EXISTS active_tariff (
                energy_type TEXT NOT NULL PRIMARY KEY,
                tarif_id INTEGER NOT NULL,
                version TEXT NOT NULL
            );
            INSERT OR IGNORE INTO active_tariff (energy_type, tarif_id, version)
            SELECT snapshot.energy_type,
                   json_extract(snapshot.payload_json, '$.tarif.idTarif'),
                   snapshot.version
            FROM tariff_snapshot AS snapshot
            WHERE snapshot.rowid = (
                SELECT latest.rowid FROM tariff_snapshot AS latest
                WHERE latest.energy_type = snapshot.energy_type
                ORDER BY latest.cached_at_utc DESC, latest.rowid DESC LIMIT 1
            );
            CREATE TABLE IF NOT EXISTS active_session (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                reservation_id INTEGER NOT NULL,
                tarif_id INTEGER NOT NULL,
                tariff_version TEXT NOT NULL,
                is_paused INTEGER NOT NULL,
                cumulative_quantity TEXT NOT NULL,
                cumulative_amount TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS consumption_reading (
                reading_id TEXT NOT NULL PRIMARY KEY,
                reservation_id INTEGER NOT NULL,
                tarif_id INTEGER NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                cumulative_quantity TEXT NOT NULL,
                amount TEXT NOT NULL,
                cumulative_amount TEXT NOT NULL DEFAULT '0'
            );
            CREATE INDEX IF NOT EXISTS ix_reading_occurred_at
                ON consumption_reading (occurred_at_utc DESC);
            CREATE TABLE IF NOT EXISTS sync_outbox (
                event_id TEXT NOT NULL PRIMARY KEY,
                event_type TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "consumption_reading", "cumulative_amount",
            "ALTER TABLE consumption_reading ADD COLUMN cumulative_amount TEXT NOT NULL DEFAULT '0';",
            cancellationToken);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task<Reservation?> GetCachedReservationAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT reservation_id, ref_id, starts_at_utc, ends_at_utc, energy_type, card_tag_hash, borne_id
            FROM cached_reservation WHERE singleton_id = 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Reservation
        {
            IdReservation = reader.GetInt32(0),
            RefId = reader.GetInt32(1),
            DateDeb = ParseUtc(reader.GetString(2)).UtcDateTime,
            DateFin = ParseUtc(reader.GetString(3)).UtcDateTime,
            TypeEnergie = Enum.Parse<TypeEnergie>(reader.GetString(4), ignoreCase: false),
            Tag = reader.GetString(5),
            IdBorne = reader.GetInt32(6)
        };
    }

    public async Task CacheReservationAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cached_reservation
                (singleton_id, reservation_id, ref_id, starts_at_utc, ends_at_utc, energy_type, card_tag_hash, borne_id)
            VALUES (1, $id, $ref, $start, $end, $energy, $tag, $borne)
            ON CONFLICT(singleton_id) DO UPDATE SET
                reservation_id = excluded.reservation_id,
                ref_id = excluded.ref_id,
                starts_at_utc = excluded.starts_at_utc,
                ends_at_utc = excluded.ends_at_utc,
                energy_type = excluded.energy_type,
                card_tag_hash = excluded.card_tag_hash,
                borne_id = excluded.borne_id;
            """;
        AddReservationParameters(command, reservation);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearReservationAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cached_reservation WHERE singleton_id = 1;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<BorneTariffSchedule?> GetTariffScheduleAsync(
        TypeEnergie energyType,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = version is null
            ? "SELECT snapshot.payload_json FROM active_tariff AS active JOIN tariff_snapshot AS snapshot ON snapshot.energy_type = active.energy_type AND snapshot.version = active.version WHERE active.energy_type = $energy;"
            : "SELECT payload_json FROM tariff_snapshot WHERE energy_type = $energy AND version = $version;";
        command.Parameters.AddWithValue("$energy", energyType.ToString());
        if (version is not null) command.Parameters.AddWithValue("$version", version);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string json
            ? JsonSerializer.Deserialize<BorneTariffSchedule>(json, JsonOptions)
            : null;
    }

    public async Task CacheTariffScheduleAsync(
        BorneTariffSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string payload = JsonSerializer.Serialize(schedule, JsonOptions);
        await using (SqliteCommand existingCommand = connection.CreateCommand())
        {
            existingCommand.Transaction = transaction;
            existingCommand.CommandText = "SELECT payload_json FROM tariff_snapshot WHERE energy_type = $energy AND version = $version;";
            existingCommand.Parameters.AddWithValue("$energy", schedule.Tarif.TypeEnergie.ToString());
            existingCommand.Parameters.AddWithValue("$version", schedule.Version);
            object? existingJson = await existingCommand.ExecuteScalarAsync(cancellationToken);
            if (existingJson is string json)
            {
                BorneTariffSchedule? existing = JsonSerializer.Deserialize<BorneTariffSchedule>(json, JsonOptions);
                if (existing is null || !SameSchedule(existing, schedule))
                {
                    throw new InvalidOperationException("A tariff version cannot be changed after it has been cached.");
                }

                await SetActiveTariffAsync(connection, transaction, schedule, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO tariff_snapshot (energy_type, version, payload_json, cached_at_utc)
            VALUES ($energy, $version, $payload, $cached)
            """;
        command.Parameters.AddWithValue("$energy", schedule.Tarif.TypeEnergie.ToString());
        command.Parameters.AddWithValue("$version", schedule.Version);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$cached", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await SetActiveTariffAsync(connection, transaction, schedule, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeactivateTariffScheduleAsync(
        int tarifId,
        TypeEnergie energyType,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = tarifId > 0
            ? "DELETE FROM active_tariff WHERE energy_type = $energy AND tarif_id = $tarif;"
            : "DELETE FROM active_tariff WHERE energy_type = $energy;";
        command.Parameters.AddWithValue("$energy", energyType.ToString());
        if (tarifId > 0) command.Parameters.AddWithValue("$tarif", tarifId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<BorneSessionState?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT reservation_id, tarif_id, tariff_version, is_paused,
                   cumulative_quantity, cumulative_amount, updated_at_utc
            FROM active_session WHERE singleton_id = 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapSession(reader) : null;
    }

    public async Task SaveSessionAsync(BorneSessionState session, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await SaveSessionAsync(connection, transaction, session, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveSessionAndQueueEventAsync(
        BorneSessionState session,
        Guid eventId,
        string eventType,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await SaveSessionAsync(connection, transaction, session, cancellationToken);
        await QueueEventAsync(connection, transaction, eventId, eventType, payloadJson, createdAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Consommation?> GetLatestReadingAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT reading_id, reservation_id, tarif_id, occurred_at_utc, cumulative_quantity, amount, cumulative_amount
            FROM consumption_reading ORDER BY occurred_at_utc DESC, reading_id DESC LIMIT 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapReading(reader) : null;
    }

    public async Task<Consommation?> GetReadingByIdAsync(Guid readingId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT reading_id, reservation_id, tarif_id, occurred_at_utc, cumulative_quantity, amount, cumulative_amount
            FROM consumption_reading WHERE reading_id = $id;
            """;
        command.Parameters.AddWithValue("$id", readingId.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapReading(reader) : null;
    }

    public async Task SaveReadingAndQueueEventAsync(
        Consommation reading,
        BorneSessionState session,
        string eventType,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO consumption_reading
                    (reading_id, reservation_id, tarif_id, occurred_at_utc, cumulative_quantity, amount, cumulative_amount)
                VALUES ($reading, $reservation, $tarif, $date, $quantity, $amount, $cumulativeAmount);
                """;
            command.Parameters.AddWithValue("$reading", reading.ReadingId.ToString("D"));
            command.Parameters.AddWithValue("$reservation", reading.ReservationId);
            command.Parameters.AddWithValue("$tarif", reading.TarifId);
            command.Parameters.AddWithValue("$date", AsUtcOffset(reading.Date).ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$quantity", reading.Quantite.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$amount", reading.Montant.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$cumulativeAmount", reading.MontantCumule.ToString(CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await SaveSessionAsync(connection, transaction, session, cancellationToken);
        await QueueEventAsync(connection, transaction, reading.ReadingId, eventType, payloadJson,
            AsUtcOffset(reading.Date), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task QueueEventAsync(
        Guid eventId,
        string eventType,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await QueueEventAsync(connection, null, eventId, eventType, payloadJson, createdAtUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingBorneEvent>> GetPendingEventsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_id, event_type, payload_json, created_at_utc, attempts
            FROM sync_outbox ORDER BY created_at_utc, event_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<PendingBorneEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new PendingBorneEvent(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                ParseUtc(reader.GetString(3)),
                reader.GetInt32(4)));
        }

        return events;
    }

    public async Task AcknowledgeEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sync_outbox WHERE event_id = $id;";
        command.Parameters.AddWithValue("$id", eventId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordEventAttemptAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE sync_outbox SET attempts = attempts + 1 WHERE event_id = $id;";
        command.Parameters.AddWithValue("$id", eventId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task SaveSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BorneSessionState session,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO active_session
                (singleton_id, reservation_id, tarif_id, tariff_version, is_paused,
                 cumulative_quantity, cumulative_amount, updated_at_utc)
            VALUES (1, $reservation, $tarif, $version, $paused, $quantity, $amount, $updated)
            ON CONFLICT(singleton_id) DO UPDATE SET
                reservation_id = excluded.reservation_id,
                tarif_id = excluded.tarif_id,
                tariff_version = excluded.tariff_version,
                is_paused = excluded.is_paused,
                cumulative_quantity = excluded.cumulative_quantity,
                cumulative_amount = excluded.cumulative_amount,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$reservation", session.ReservationId);
        command.Parameters.AddWithValue("$tarif", session.TarifId);
        command.Parameters.AddWithValue("$version", session.TarifVersion);
        command.Parameters.AddWithValue("$paused", session.IsPaused ? 1 : 0);
        command.Parameters.AddWithValue("$quantity", session.LastCumulativeQuantity.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$amount", session.LastCumulativeAmount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated", session.UpdatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueueEventAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid eventId,
        string eventType,
        string payloadJson,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO sync_outbox (event_id, event_type, payload_json, created_at_utc, attempts)
            VALUES ($id, $type, $payload, $created, 0);
            """;
        command.Parameters.AddWithValue("$id", eventId.ToString("D"));
        command.Parameters.AddWithValue("$type", eventType);
        command.Parameters.AddWithValue("$payload", payloadJson);
        command.Parameters.AddWithValue("$created", createdAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string alterSql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        bool exists = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                exists = true;
                break;
            }
        }

        await reader.DisposeAsync();
        if (!exists)
        {
            await using SqliteCommand alter = connection.CreateCommand();
            alter.CommandText = alterSql;
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddReservationParameters(SqliteCommand command, Reservation reservation)
    {
        command.Parameters.AddWithValue("$id", reservation.IdReservation);
        command.Parameters.AddWithValue("$ref", reservation.RefId);
        command.Parameters.AddWithValue("$start", AsUtcOffset(reservation.DateDeb).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$end", AsUtcOffset(reservation.DateFin).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$energy", reservation.TypeEnergie.ToString());
        command.Parameters.AddWithValue("$tag", reservation.Tag!);
        command.Parameters.AddWithValue("$borne", reservation.IdBorne);
    }

    private static bool SameSchedule(BorneTariffSchedule left, BorneTariffSchedule right)
    {
        if (left.Version != right.Version ||
            left.Tarif.IdTarif != right.Tarif.IdTarif ||
            left.Tarif.TypeEnergie != right.Tarif.TypeEnergie)
        {
            return false;
        }

        Tranche[] leftBands = left.Tranches.OrderBy(band => band.ValMin).ToArray();
        Tranche[] rightBands = right.Tranches.OrderBy(band => band.ValMin).ToArray();
        return leftBands.Length == rightBands.Length && leftBands.Zip(rightBands).All(pair =>
            pair.First.IdTranche == pair.Second.IdTranche &&
            pair.First.IdTarif == pair.Second.IdTarif &&
            pair.First.ValMin == pair.Second.ValMin &&
            pair.First.ValMax == pair.Second.ValMax &&
            pair.First.Prix == pair.Second.Prix);
    }

    private static async Task SetActiveTariffAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BorneTariffSchedule schedule,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO active_tariff (energy_type, tarif_id, version)
            VALUES ($energy, $tarif, $version)
            ON CONFLICT(energy_type) DO UPDATE SET tarif_id = excluded.tarif_id, version = excluded.version;
            """;
        command.Parameters.AddWithValue("$energy", schedule.Tarif.TypeEnergie.ToString());
        command.Parameters.AddWithValue("$tarif", schedule.Tarif.IdTarif);
        command.Parameters.AddWithValue("$version", schedule.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static BorneSessionState MapSession(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetString(2),
        reader.GetInt32(3) != 0,
        decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
        decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
        ParseUtc(reader.GetString(6)));

    private static Consommation MapReading(SqliteDataReader reader) => new()
    {
        ReadingId = Guid.Parse(reader.GetString(0)),
        ReservationId = reader.GetInt32(1),
        TarifId = reader.GetInt32(2),
        Date = ParseUtc(reader.GetString(3)).UtcDateTime,
        Quantite = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
        Montant = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
        MontantCumule = decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture)
    };

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
