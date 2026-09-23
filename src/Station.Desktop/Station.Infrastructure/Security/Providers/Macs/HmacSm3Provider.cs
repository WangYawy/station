using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security.Providers.Macs;

/// <summary>HMAC-SM3 实现（国密对称 MAC）。</summary>
public sealed class HmacSm3Provider : IMacProvider
{
    public string Algorithm => CryptoAlgorithm.HmacSm3;

    public string Compute(byte[] key, byte[] data)
    {
        var mac = new HMac(new SM3Digest());
        mac.Init(new KeyParameter(key));
        mac.BlockUpdate(data, 0, data.Length);
        var output = new byte[mac.GetMacSize()];
        mac.DoFinal(output, 0);
        return Convert.ToBase64String(output);
    }

    public bool Verify(byte[] key, byte[] data, string macBase64)
    {
        try
        {
            var expected = Convert.FromBase64String(macBase64);
            var mac = new HMac(new SM3Digest());
            mac.Init(new KeyParameter(key));
            mac.BlockUpdate(data, 0, data.Length);
            var actual = new byte[mac.GetMacSize()];
            mac.DoFinal(actual, 0);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
