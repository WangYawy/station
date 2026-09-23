using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

/// <summary>
/// 文件摘要 + 元数据签名服务。
/// 
/// 【摘要】走 file_sig 策略的 Algorithm（默认 SM3）。
/// 【签名】走 file_sig 策略的 SecondaryAlgorithm（默认 SM2-SM3）。
/// 【私钥】从 ReportingOptions.PrivateKeyFile 读（PEM）。
/// </summary>
public sealed class FileSigningService : IFileSigningService
{
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoProviderFactory _factory;
    private readonly IKeyFileResolver _keyResolver;
    private readonly ReportingOptions _reportingOptions;
    private readonly ILogger<FileSigningService> _logger;

    public FileSigningService(
        ICryptoPolicyService policy,
        ICryptoProviderFactory factory,
        IKeyFileResolver keyResolver,
        IOptions<ReportingOptions> reportingOptions,
        ILogger<FileSigningService> logger)
    {
        _policy = policy;
        _factory = factory;
        _keyResolver = keyResolver;
        _reportingOptions = reportingOptions.Value;
        _logger = logger;
    }

    public async Task<(string Digest, string Algorithm)> ComputeDigestAsync(
        string filePath, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var hasher = _factory.GetHasher(policy.Algorithm);

        await using var fs = File.OpenRead(filePath);
        var digest = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return (digest, policy.Algorithm);
    }

    public async Task<(string Signature, string Algorithm)> SignMetadataAsync(
        string metadataJson, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var signAlgo = policy.SecondaryAlgorithm ?? CryptoAlgorithm.Sm2Sm3;

        var privateKeyPem = await _keyResolver.ResolveAsync(_reportingOptions.PrivateKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException(
                $"未配置元数据签名私钥：{_reportingOptions.PrivateKeyFile}");

        var signer = _factory.GetSigner(signAlgo);
        var signature = signer.Sign(Encoding.UTF8.GetBytes(metadataJson), privateKeyPem);

        _logger.LogDebug("元数据签名完成（算法 {Algo}）", signAlgo);
        return (signature, signAlgo);
    }

    public async Task<bool> VerifyMetadataAsync(
        string metadataJson, string signature, string signAlgorithm,
        CancellationToken ct = default)
    {
        var publicKeyPem = await _keyResolver.ResolveAsync(_reportingOptions.PublicKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return false;

        var signer = _factory.GetSigner(signAlgorithm);
        return signer.Verify(Encoding.UTF8.GetBytes(metadataJson), signature, publicKeyPem);
    }
}
