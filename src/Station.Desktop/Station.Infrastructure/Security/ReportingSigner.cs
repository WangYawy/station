using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

/// <summary>上报签名实现（走 CryptoUsage.Reporting 策略）。</summary>
public sealed class ReportingSigner : IReportingSigner
{
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoProviderFactory _factory;
    private readonly IKeyFileResolver _keyResolver;
    private readonly ReportingOptions _options;
    private readonly ILogger<ReportingSigner> _logger;

    public ReportingSigner(
        ICryptoPolicyService policy,
        ICryptoProviderFactory factory,
        IKeyFileResolver keyResolver,
        IOptions<ReportingOptions> options,
        ILogger<ReportingSigner> logger)
    {
        _policy = policy;
        _factory = factory;
        _keyResolver = keyResolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(string? Signature, string? Algorithm)> SignAsync(
        string canonicalJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PrivateKeyFile))
            return (null, null);

        var policy = await _policy.GetAsync(CryptoUsage.Reporting, ct).ConfigureAwait(false);
        var signAlgo = policy.Algorithm;

        var privateKeyPem = await _keyResolver.ResolveAsync(_options.PrivateKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            _logger.LogWarning("上报私钥文件为空：{Path}", _options.PrivateKeyFile);
            return (null, null);
        }

        var signer = _factory.GetSigner(signAlgo);
        var signature = signer.Sign(Encoding.UTF8.GetBytes(canonicalJson), privateKeyPem);
        return (signature, signAlgo);
    }

    public async Task<bool> VerifyAsync(
        string canonicalJson, string signature, string algorithm,
        CancellationToken ct = default)
    {
        var publicKeyPem = await _keyResolver.ResolveAsync(_options.PublicKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(publicKeyPem)) return false;

        var signer = _factory.GetSigner(algorithm);
        return signer.Verify(Encoding.UTF8.GetBytes(canonicalJson), signature, publicKeyPem);
    }
}
