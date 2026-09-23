using System.Buffers;
using System.Security.Cryptography;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security.Providers.Hashers;

/// <summary>SHA-256 摘要（非国密备选）。返回 Base64 字符串。</summary>
public sealed class Sha256Hasher : IHasher
{
    public string Algorithm => CryptoAlgorithm.Sha256;

    public string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToBase64String(hash);
    }

    public async Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }
}
