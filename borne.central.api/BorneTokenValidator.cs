using System.Security.Cryptography;
using System.Text;

namespace borne.central.api;

public sealed class BorneTokenValidator(IConfiguration configuration)
{
    public bool IsValid(string borneId, string? token)
    {
        string? expected = configuration[$"CentralApi:BorneTokens:{borneId}"];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(token)) return false;
        byte[] a = Encoding.UTF8.GetBytes(expected);
        byte[] b = Encoding.UTF8.GetBytes(token);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public bool IsMonitoringTokenValid(string? token)
        => IsTokenValid("CentralApi:MonitoringToken", token);

    public bool IsTariffManagementTokenValid(string? token)
        => IsTokenValid("CentralApi:TariffManagementToken", token);

    private bool IsTokenValid(string configurationKey, string? token)
    {
        string? expected = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(token)) return false;
        byte[] a = Encoding.UTF8.GetBytes(expected);
        byte[] b = Encoding.UTF8.GetBytes(token);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
