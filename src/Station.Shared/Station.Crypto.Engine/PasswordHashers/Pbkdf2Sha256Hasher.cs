using System.Security.Cryptography;

namespace Station.Crypto.Engine.PasswordHashers;

/// <summary>
/// PBKDF2-SHA256。
/// 存储格式：pbkdf2-sha256$迭代次数$盐(Base64)$哈希(Base64)。
/// </summary>
public sealed class Pbkdf2Sha256Hasher : IPasswordHasher
{
    /// <summary>默认迭代次数。</summary>
    public const int DefaultIterations = 100_000;

    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Algorithm => CryptoAlgorithm.Pbkdf2Sha256;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, DefaultIterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash)) return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    public bool NeedsRehash(string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return true;
        if (!int.TryParse(parts[1], out var iterations)) return true;
        return iterations < DefaultIterations;
    }
}
