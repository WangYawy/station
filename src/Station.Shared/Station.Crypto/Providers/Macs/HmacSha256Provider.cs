using System.Security.Cryptography;
using Station.Crypto.Abstractions;
using Station.Crypto.Algorithms;

namespace Station.Crypto.Providers.Macs;

/// <summary>HMAC-SHA256 实现。</summary>
public sealed class HmacSha256Provider : IMacProvider
{
    public string Algorithm => CryptoAlgorithm.HmacSha256;

    public string Compute(byte[] key, byte[] data)
        => Convert.ToBase64String(HMACSHA256.HashData(key, data));

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
