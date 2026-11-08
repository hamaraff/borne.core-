using borne.infrastructure.Data;
using MySqlConnector;

string? connectionString =
    Environment.GetEnvironmentVariable("BORNE_DB_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Set BORNE_DB_CONNECTION_STRING before running the connection check."
    );
    return 1;
}

var factory = new MySqlConnectionFactory(connectionString);

try
{
    await using var connection = factory.CreateConnection();
    await connection.OpenAsync();

    Console.WriteLine("Database connection successful!");
    Console.WriteLine($"Server: {connection.ServerVersion}");
    return 0;
}
catch (MySqlException)
{
    Console.Error.WriteLine(
        "Database connection failed. Check the local connection settings and server availability."
    );
    return 1;
}
