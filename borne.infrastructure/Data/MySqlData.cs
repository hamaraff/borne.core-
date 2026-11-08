using MySqlConnector;
using System.Data;

namespace borne.infrastructure.Data;

internal static class MySqlData
{
    public static void AddParameter(
        MySqlCommand command,
        string name,
        MySqlDbType type,
        object? value)
    {
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
    }

    public static DateTime ToDatabaseUtc(DateTime value)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return DateTime.SpecifyKind(
            utc.AddTicks(-(utc.Ticks % 10)),
            DateTimeKind.Utc
        );
    }

    public static DateTime ReadUtcDateTime(
        MySqlDataReader reader,
        string column)
    {
        return DateTime.SpecifyKind(
            reader.GetDateTime(column),
            DateTimeKind.Utc
        );
    }

    public static TEnum ReadEnum<TEnum>(
        MySqlDataReader reader,
        string column)
        where TEnum : struct, Enum
    {
        string value = reader.GetString(column);
        if (!Enum.TryParse(value, ignoreCase: false, out TEnum parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new DataException(
                $"Database column '{column}' contains an unsupported {typeof(TEnum).Name} value."
            );
        }

        return parsed;
    }

    public static string? ReadNullableString(
        MySqlDataReader reader,
        string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
