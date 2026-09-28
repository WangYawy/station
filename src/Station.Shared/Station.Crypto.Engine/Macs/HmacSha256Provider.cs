using System.Security.Cryptography;

namespace Station.Crypto.Engine.Macs;

/// <summary>HMAC-SHA256 实现。</summary>
public sealed class HmacSha256Provider : IMacProvider
{
    public string Algorithm => CryptoAlgorithm.HmacSha256;

    public string Compute(byte[] key, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToBase64String(HMACSHA256.HashData(key, data));
    }

    public bool Verify(byte[] key, byte[] data, string macBase64)
    {
        if (key is null || data is null || string.IsNullOrEmpty(macBase64)) return false;

        byte[] expected;
        try { expected = Convert.FromBase64String(macBase64); }
        catch (FormatException) { return false; }

        var actual = HMACSHA256.HashData(key, data);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
