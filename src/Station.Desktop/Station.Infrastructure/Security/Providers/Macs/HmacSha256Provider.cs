using System.Security.Cryptography;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security.Providers.Macs;

/// <summary>HMAC-SHA256 实现（非国密备选）。</summary>
public sealed class HmacSha256Provider : IMacProvider
{
    public string Algorithm => CryptoAlgorithm.HmacSha256;

    public string Compute(byte[] key, byte[] data)
    {
        var mac = HMACSHA256.HashData(key, data);
        return Convert.ToBase64String(mac);
    }

    public bool Verify(byte[] key, byte[] data, string macBase64)
    {
        try
        {
            var expected = Convert.FromBase64String(macBase64);
            var actual = HMACSHA256.HashData(key, data);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
