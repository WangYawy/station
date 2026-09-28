using System.Security.Cryptography;
using System.Text;

namespace Station.Crypto.Engine.PasswordHashers;

/// <summary>
/// MD5 兼容实现（仅用于旧数据校验，禁止新写入）。
/// </summary>
public sealed class Md5LegacyHasher : IPasswordHasher
{
    public string Algorithm => CryptoAlgorithm.Md5;

    /// <summary>MD5 属于遗留算法，禁止新写入。</summary>
    public string Hash(string password) =>
        throw new NotSupportedException("MD5 仅用于兼容旧数据，不允许新写入。");

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;

        // md5$salt$hash
        var parts = storedHash.Split('$');
        if (parts.Length == 3 && parts[0].Equals("md5", StringComparison.OrdinalIgnoreCase))
        {
            var salt = parts[1];
            var expected = parts[2];
            var actual = Md5Hex(salt + password);
            return FixedTimeHexEquals(actual, expected);
        }

        // 纯 32 位十六进制
        if (storedHash.Length == 32 && IsHex(storedHash))
        {
            var actual = Md5Hex(password);
            return FixedTimeHexEquals(actual, storedHash);
        }

        return false;
    }

    /// <summary>MD5 属于遗留算法，始终建议重哈希。</summary>
    public bool NeedsRehash(string storedHash) => true;

    private static string Md5Hex(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsHex(string s) =>
        s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    private static bool FixedTimeHexEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= char.ToLowerInvariant(a[i]) ^ char.ToLowerInvariant(b[i]);
        return diff == 0;
    }
}
