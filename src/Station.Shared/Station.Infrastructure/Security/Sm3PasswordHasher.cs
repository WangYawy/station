using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace Station.Infrastructure.Security;

/// <summary>
/// SM3 密码哈希：PBKDF2-HMAC-SM3（国密合规，抗暴力破解）。
/// 存储格式：sm3$迭代次数$盐(Base64)$哈希(Base64)。
/// </summary>
public sealed class Sm3PasswordHasher : IPasswordHasher
{
    private const int Iterations = 10000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Pbkdf2Sm3(password, salt, Iterations, HashSize);
        return $"sm3${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "sm3")
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Pbkdf2Sm3(password, salt, iterations, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
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
                for (var k = 0; k < blockSize; k++)
                {
                    t[k] ^= u[k];
                }
            }

            var copyLen = Math.Min(blockSize, length - (i - 1) * blockSize);
            Array.Copy(t, 0, output, (i - 1) * blockSize, copyLen);
        }

        return output;
    }
}
