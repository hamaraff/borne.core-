using borne.core.Entities;
using borne.core.IInfra;
using borne.infrastructure.Data;
using MySqlConnector;

namespace borne.infrastructure.Daos;

public sealed class MySqlConsommationDAO : IConsommationDAO
{
    private const string SelectFrom =
        "SELECT c.id_consommation, c.reading_id, c.date_consommation, c.quantite, " +
        "c.montant, c.tarif_id, c.reservation_id " +
        "FROM consommation c JOIN reservation r ON r.id_reservation = c.reservation_id";

    private readonly MySqlConnectionFactory connectionFactory;

    public MySqlConsommationDAO(MySqlConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public Task<Consommation?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(
            "WHERE c.id_consommation = @id",
            command => MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, id),
            cancellationToken
        );
    }

    public Task<Consommation?> GetByReadingIdAsync(
        Guid readingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(readingId, Guid.Empty);
        return GetSingleAsync(
            "WHERE c.reading_id = @readingId",
            command => AddReadingId(command, readingId),
            cancellationToken
        );
    }

    public Task<List<Consommation>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return GetManyAsync(
            "ORDER BY c.date_consommation, c.id_consommation",
            cancellationToken: cancellationToken
        );
    }

    public Task<List<Consommation>> GetByReservationIdAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
    {
        return GetManyAsync(
            "WHERE c.reservation_id = @reservationId " +
            "ORDER BY c.date_consommation, c.id_consommation",
            command => MySqlData.AddParameter(
                command,
                "@reservationId",
                MySqlDbType.Int32,
                reservationId
            ),
            cancellationToken
        );
    }

    public Task<List<Consommation>> GetByBorneIdAsync(
        int idBorne,
        CancellationToken cancellationToken = default)
    {
        return GetManyAsync(
            "WHERE r.id_borne = @idBorne " +
            "ORDER BY c.date_consommation, c.id_consommation",
            command => MySqlData.AddParameter(command, "@idBorne", MySqlDbType.Int32, idBorne),
            cancellationToken
        );
    }

    public Task<List<Consommation>> GetByBorneIdAndDateRangeAsync(
        int idBorne,
        DateTime dateDebut,
        DateTime dateFin,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(dateDebut, dateFin);
        return GetManyAsync(
            "WHERE r.id_borne = @idBorne " +
            "AND c.date_consommation >= @dateDebut " +
            "AND c.date_consommation < @dateFin " +
            "ORDER BY c.date_consommation, c.id_consommation",
            command =>
            {
                MySqlData.AddParameter(command, "@idBorne", MySqlDbType.Int32, idBorne);
                AddDateRangeParameters(command, dateDebut, dateFin);
            },
            cancellationToken
        );
    }

    public Task<Consommation?> GetLatestByBorneIdAsync(
        int idBorne,
        CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(
            "WHERE r.id_borne = @idBorne " +
            "ORDER BY c.date_consommation DESC, c.id_consommation DESC LIMIT 1",
            command => MySqlData.AddParameter(command, "@idBorne", MySqlDbType.Int32, idBorne),
            cancellationToken
        );
    }

    public async Task<List<Consommation>> GetByDateAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        DateTime startUtc = MySqlData.ToDatabaseUtc(date).Date;
        DateTime endUtc = startUtc.AddDays(1);
        return await GetByDateRangeAsync(startUtc, endUtc, cancellationToken);
    }

    public Task<List<Consommation>> GetByDateRangeAsync(
        DateTime dateDebut,
        DateTime dateFin,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(dateDebut, dateFin);
        return GetManyAsync(
            "WHERE c.date_consommation >= @dateDebut " +
            "AND c.date_consommation < @dateFin " +
            "ORDER BY c.date_consommation, c.id_consommation",
            command => AddDateRangeParameters(command, dateDebut, dateFin),
            cancellationToken
        );
    }

    public async Task<bool> AddAsync(
        Consommation consommation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consommation);
        ArgumentOutOfRangeException.ThrowIfEqual(consommation.ReadingId, Guid.Empty);

        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(
                "INSERT INTO consommation " +
                "(reading_id, date_consommation, quantite, montant, tarif_id, reservation_id) " +
                "VALUES (@readingId, @date, @quantity, @amount, @tarifId, @reservationId)",
                connection
            );
            AddConsumptionParameters(command, consommation, includeReadingId: true);
            await command.ExecuteNonQueryAsync(cancellationToken);
            consommation.IdConsommation = checked((int)command.LastInsertedId);
            return true;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            Consommation? existing = await GetByReadingIdAsync(
                consommation.ReadingId,
                cancellationToken
            );
            if (existing is null || !SameReading(existing, consommation))
            {
                throw new InvalidOperationException(
                    "A reading ID was reused with different reading data.",
                    exception
                );
            }

            consommation.IdConsommation = existing.IdConsommation;
            return false;
        }
    }

    public async Task UpdateAsync(
        Consommation consommation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consommation);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "UPDATE consommation SET date_consommation = @date, quantite = @quantity, " +
            "montant = @amount, tarif_id = @tarifId, reservation_id = @reservationId " +
            "WHERE id_consommation = @id",
            connection
        );
        AddConsumptionParameters(command, consommation, includeReadingId: false);
        MySqlData.AddParameter(
            command,
            "@id",
            MySqlDbType.Int32,
            consommation.IdConsommation
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "DELETE FROM consommation WHERE id_consommation = @id",
            connection
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Consommation?> GetSingleAsync(
        string predicate,
        Action<MySqlCommand>? addParameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"{SelectFrom} {predicate}",
            connection
        );
        addParameters?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private async Task<List<Consommation>> GetManyAsync(
        string suffix,
        Action<MySqlCommand>? addParameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"{SelectFrom} {suffix}",
            connection
        );
        addParameters?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var readings = new List<Consommation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            readings.Add(Map(reader));
        }

        return readings;
    }

    private static Consommation Map(MySqlDataReader reader)
    {
        return new Consommation
        {
            IdConsommation = reader.GetInt32("id_consommation"),
            ReadingId = Guid.Parse(reader.GetString("reading_id")),
            Date = MySqlData.ReadUtcDateTime(reader, "date_consommation"),
            Quantite = reader.GetDecimal("quantite"),
            Montant = reader.GetDecimal("montant"),
            TarifId = reader.GetInt32("tarif_id"),
            ReservationId = reader.GetInt32("reservation_id")
        };
    }

    private static void AddConsumptionParameters(
        MySqlCommand command,
        Consommation consommation,
        bool includeReadingId)
    {
        if (includeReadingId)
        {
            AddReadingId(command, consommation.ReadingId);
        }

        MySqlData.AddParameter(
            command,
            "@date",
            MySqlDbType.DateTime,
            MySqlData.ToDatabaseUtc(consommation.Date)
        );
        MySqlData.AddParameter(
            command,
            "@quantity",
            MySqlDbType.Decimal,
            consommation.Quantite
        );
        MySqlData.AddParameter(
            command,
            "@amount",
            MySqlDbType.Decimal,
            consommation.Montant
        );
        MySqlData.AddParameter(
            command,
            "@tarifId",
            MySqlDbType.Int32,
            consommation.TarifId
        );
        MySqlData.AddParameter(
            command,
            "@reservationId",
            MySqlDbType.Int32,
            consommation.ReservationId
        );
    }

    private static void AddDateRangeParameters(
        MySqlCommand command,
        DateTime dateDebut,
        DateTime dateFin)
    {
        MySqlData.AddParameter(
            command,
            "@dateDebut",
            MySqlDbType.DateTime,
            MySqlData.ToDatabaseUtc(dateDebut)
        );
        MySqlData.AddParameter(
            command,
            "@dateFin",
            MySqlDbType.DateTime,
            MySqlData.ToDatabaseUtc(dateFin)
        );
    }

    private static void AddReadingId(MySqlCommand command, Guid readingId)
    {
        MySqlData.AddParameter(
            command,
            "@readingId",
            MySqlDbType.VarChar,
            readingId.ToString("D")
        );
    }

    private static bool SameReading(
        Consommation persisted,
        Consommation submitted)
    {
        return persisted.ReservationId == submitted.ReservationId &&
            persisted.TarifId == submitted.TarifId &&
            persisted.Quantite == submitted.Quantite &&
            persisted.Montant == submitted.Montant &&
            persisted.Date == MySqlData.ToDatabaseUtc(submitted.Date);
    }

    private static void ValidateDateRange(DateTime dateDebut, DateTime dateFin)
    {
        if (dateFin < dateDebut)
        {
            throw new ArgumentException(
                "The end date must be later than or equal to the start date."
            );
        }
    }
}
