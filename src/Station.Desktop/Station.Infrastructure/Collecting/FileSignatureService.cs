using Microsoft.Extensions.Logging;
using Station.Application.Collecting;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Collecting;

public sealed class FileSignatureService : IFileSignatureService
{
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoProviderFactory _factory;
    private readonly ILogger<FileSignatureService> _logger;

    public FileSignatureService(
        ICryptoPolicyService policy,
        ICryptoProviderFactory factory,
        ILogger<FileSignatureService> logger)
    {
        _policy = policy;
        _factory = factory;
        _logger = logger;
    }

    public async Task<(string Digest, string DigestAlgo, string? Signature)> SignFileAsync(
        string filePath, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct);
        var digestAlgo = policy.Algorithm; // 默认 SM3
        var signAlgo = policy.SecondaryAlgorithm ?? CryptoAlgorithm.Sm2Sm3;

        var hasher = _factory.GetHasher(digestAlgo);
        await using var fs = File.OpenRead(filePath);
        var digest = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);

        // 签名可选（视部署是否需要；默认不签，只算摘要）
        string? signature = null;
        return (digest, digestAlgo, signature);
    }

    public async Task<bool> VerifyFileAsync(string filePath, string expectedDigest,
        string digestAlgo, CancellationToken ct = default)
    {
        var hasher = _factory.GetHasher(digestAlgo);
        await using var fs = File.OpenRead(filePath);
        var actual = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return string.Equals(actual, expectedDigest, StringComparison.Ordinal);
    }
}
