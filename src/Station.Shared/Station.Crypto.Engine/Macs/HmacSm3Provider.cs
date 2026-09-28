using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace Station.Crypto.Engine.Macs;

/// <summary>HMAC-SM3 实现。</summary>
public sealed class HmacSm3Provider : IMacProvider
{
    public string Algorithm => CryptoAlgorithm.HmacSm3;

    public string Compute(byte[] key, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);

        var mac = new HMac(new SM3Digest());
        mac.Init(new KeyParameter(key));
        mac.BlockUpdate(data, 0, data.Length);
        var output = new byte[mac.GetMacSize()];
        mac.DoFinal(output, 0);
        return Convert.ToBase64String(output);
    }

    public bool Verify(byte[] key, byte[] data, string macBase64)
    {
        if (key is null || data is null || string.IsNullOrEmpty(macBase64)) return false;

        byte[] expected;
        try { expected = Convert.FromBase64String(macBase64); }
        catch (FormatException) { return false; }

        var mac = new HMac(new SM3Digest());
        mac.Init(new KeyParameter(key));
        mac.BlockUpdate(data, 0, data.Length);
        var actual = new byte[mac.GetMacSize()];
        mac.DoFinal(actual, 0);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
