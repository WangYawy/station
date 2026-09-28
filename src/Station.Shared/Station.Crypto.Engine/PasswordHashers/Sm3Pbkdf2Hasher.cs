using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace Station.Crypto.Engine.PasswordHashers;

/// <summary>
/// PBKDF2-HMAC-SM3 密码哈希。
/// 存储格式：sm3$迭代次数$盐(Base64)$哈希(Base64)。
/// </summary>
public sealed class Sm3Pbkdf2Hasher : IPasswordHasher
{
    /// <summary>默认迭代次数。</summary>
    public const int DefaultIterations = 10_000;

    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Algorithm => CryptoAlgorithm.Sm3Pbkdf2;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Pbkdf2Sm3(password, salt, DefaultIterations, HashSize);
        return $"sm3${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "sm3") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Pbkdf2Sm3(password, salt, iterations, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    public bool NeedsRehash(string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "sm3") return true;
        if (!int.TryParse(parts[1], out var iterations)) return true;
        return iterations < DefaultIterations;
    }

    private static byte[] Pbkdf2Sm3(string password, byte[] salt, int iterations, int length)
    {
        var hmac = new HMac(new SM3Digest());
        hmac.Init(new KeyParameter(Encoding.UTF8.GetBytes(password)));

        var blockSize = hmac.GetMacSize();
        var blockCount = (length + blockSize - 1) / blockSize;
        var output = new byte[length];
        var u = new byte[blockSize];
        var t = new byte[blockSize];

        for (var i = 1; i <= blockCount; i++)
        {
            hmac.Reset();
            hmac.BlockUpdate(salt, 0, salt.Length);
            hmac.Update((byte)(i >> 24));
            hmac.Update((byte)(i >> 16));
            hmac.Update((byte)(i >> 8));
            hmac.Update((byte)i);
            hmac.DoFinal(u, 0);
            Array.Copy(u, t, blockSize);

            for (var j = 1; j < iterations; j++)
            {
                hmac.Reset();
                hmac.BlockUpdate(u, 0, u.Length);
                hmac.DoFinal(u, 0);
                for (var k = 0; k < blockSize; k++) t[k] ^= u[k];
            }

            var copyLen = Math.Min(blockSize, length - (i - 1) * blockSize);
            Array.Copy(t, 0, output, (i - 1) * blockSize, copyLen);
        }

        return output;
    }
}
