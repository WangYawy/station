using System.Security.Cryptography;

namespace Station.Crypto.Engine.Hashers;

/// <summary>SHA-256 摘要。</summary>
public sealed class Sha256Hasher : IHasher
{
    public string Algorithm => CryptoAlgorithm.Sha256;

    public string ComputeHash(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToBase64String(SHA256.HashData(data));
    }

    public async Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }
}
