using borne.core.Entities;
using borne.core.Enums;
using borne.core.IInfra;
using borne.infrastructure.Data;
using MySqlConnector;

namespace borne.infrastructure.Daos;

public sealed class MySqlReservationDAO : IReservationDAO
{
    private const string SelectColumns =
        "id_reservation, ref_id, date_deb, date_fin, type_energie, tag, id_borne";

    private readonly MySqlConnectionFactory connectionFactory;

    public MySqlReservationDAO(MySqlConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<Reservation?> GetByIdAsync(
        int idReservation,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM reservation WHERE id_reservation = @id",
            connection
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, idReservation);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<List<Reservation>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            $"SELECT {SelectColumns} FROM reservation ORDER BY id_reservation",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var reservations = new List<Reservation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reservations.Add(Map(reader));
        }

        return reservations;
    }

    public async Task AddAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateEnergyType(reservation.TypeEnergie);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "INSERT INTO reservation (ref_id, date_deb, date_fin, type_energie, tag, id_borne) " +
            "VALUES (@refId, @dateDe, @dateFin, @energy, @tag, @borneId)",
            connection
        );
        AddReservationParameters(command, reservation);
        await command.ExecuteNonQueryAsync(cancellationToken);
        reservation.IdReservation = checked((int)command.LastInsertedId);
    }

    public async Task UpdateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ValidateEnergyType(reservation.TypeEnergie);

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "UPDATE reservation SET ref_id = @refId, date_deb = @dateDe, date_fin = @dateFin, " +
            "type_energie = @energy, tag = @tag, id_borne = @borneId " +
            "WHERE id_reservation = @id",
            connection
        );
        AddReservationParameters(command, reservation);
        MySqlData.AddParameter(
            command,
            "@id",
            MySqlDbType.Int32,
            reservation.IdReservation
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        int idReservation,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "DELETE FROM reservation WHERE id_reservation = @id",
            connection
        );
        MySqlData.AddParameter(command, "@id", MySqlDbType.Int32, idReservation);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Reservation Map(MySqlDataReader reader)
    {
        return new Reservation
        {
            IdReservation = reader.GetInt32("id_reservation"),
            RefId = reader.GetInt32("ref_id"),
            DateDeb = MySqlData.ReadUtcDateTime(reader, "date_deb"),
            DateFin = MySqlData.ReadUtcDateTime(reader, "date_fin"),
            TypeEnergie = MySqlData.ReadEnum<TypeEnergie>(reader, "type_energie"),
            Tag = reader.IsDBNull(reader.GetOrdinal("tag"))
                ? null
                : reader.GetString("tag"),
            IdBorne = reader.GetInt32("id_borne")
        };
    }

    private static void AddReservationParameters(
        MySqlCommand command,
        Reservation reservation)
    {
        MySqlData.AddParameter(command, "@refId", MySqlDbType.Int32, reservation.RefId);
        MySqlData.AddParameter(
            command,
            "@dateDe",
            MySqlDbType.DateTime,
            MySqlData.ToDatabaseUtc(reservation.DateDeb)
        );
        MySqlData.AddParameter(
            command,
            "@dateFin",
            MySqlDbType.DateTime,
            MySqlData.ToDatabaseUtc(reservation.DateFin)
        );
        MySqlData.AddParameter(
            command,
            "@energy",
            MySqlDbType.VarChar,
            reservation.TypeEnergie.ToString()
        );
        MySqlData.AddParameter(command, "@tag", MySqlDbType.VarChar, reservation.Tag);
        MySqlData.AddParameter(command, "@borneId", MySqlDbType.Int32, reservation.IdBorne);
    }

    private static void ValidateEnergyType(TypeEnergie typeEnergie)
    {
        if (!Enum.IsDefined(typeEnergie))
        {
            throw new ArgumentOutOfRangeException(nameof(typeEnergie));
        }
    }
}
