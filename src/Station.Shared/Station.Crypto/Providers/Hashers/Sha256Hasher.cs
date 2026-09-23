using System.Security.Cryptography;
using Station.Crypto.Abstractions;
using Station.Crypto.Algorithms;

namespace Station.Crypto.Providers.Hashers;

/// <summary>SHA-256 摘要。</summary>
public sealed class Sha256Hasher : IHasher
{
    public string Algorithm => CryptoAlgorithm.Sha256;

    public string ComputeHash(byte[] data)
        => Convert.ToBase64String(SHA256.HashData(data));

    public async Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }
}
