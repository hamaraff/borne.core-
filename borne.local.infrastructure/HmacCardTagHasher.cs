using System.Security.Cryptography;
using System.Text;
using borne.core.IInfra;

namespace borne.local.infrastructure;

public sealed class HmacCardTagHasher : ICardTagHasher
{
    private readonly byte[] key;

    public HmacCardTagHasher(string keyMaterial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyMaterial);
        key = Encoding.UTF8.GetBytes(keyMaterial);
        if (key.Length < 32)
        {
            throw new ArgumentException("The card tag HMAC key must contain at least 32 UTF-8 bytes.", nameof(keyMaterial));
        }
    }

    public string Hash(string cardTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardTag);
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(cardTag)));
    }
}
