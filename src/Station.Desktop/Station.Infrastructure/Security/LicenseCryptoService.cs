using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Crypto;
using Station.Crypto.Engine.Licensing;
using Station.Infrastructure.Security.Keys;

namespace Station.Infrastructure.Security;

/// <summary>授权加密服务实现（只签名不加密）。</summary>
public sealed class LicenseCryptoService : ILicenseCryptoService
{
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoAlgorithmRegistry _registry;
    private readonly PemKeyCache _keyCache;
    private readonly LicenseOptions _licenseOptions;
    private readonly ReportingOptions _reportingOptions;
    private readonly ILogger<LicenseCryptoService> _logger;

    public LicenseCryptoService(
        ICryptoPolicyService policy,
        ICryptoAlgorithmRegistry registry,
        PemKeyCache keyCache,
        IOptions<LicenseOptions> licenseOptions,
        IOptions<ReportingOptions> reportingOptions,
        ILogger<LicenseCryptoService> logger)
    {
        _policy = policy;
        _registry = registry;
        _keyCache = keyCache;
        _licenseOptions = licenseOptions.Value;
        _reportingOptions = reportingOptions.Value;
        _logger = logger;
    }

    // =========================================================
    // 授权文件生成（薄壳，核心逻辑在 LicenseEngine）
    // =========================================================

    public async Task<string> GenerateLicenseAsync(
        LicensePayload payload, CancellationToken ct = default)
    {
        var signer = await _policy.GetSignerAsync(CryptoUsage.License, ct).ConfigureAwait(false);

        var privateKeyPem = await _keyCache.GetAsync(_licenseOptions.PrivateKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException(
                $"未配置授权签名私钥：{_licenseOptions.PrivateKeyFile}");

        var json = LicenseEngine.Build(payload, privateKeyPem, signer);

        _logger.LogInformation("授权文件已生成：{Key}（算法 {Algo}）",
            payload.LicenseKey, signer.Algorithm);

        return json;
    }

    // =========================================================
    // 授权文件验证（薄壳，核心逻辑在 LicenseEngine）
    // =========================================================

    public async Task<LicenseValidateResult> ValidateLicenseAsync(
        string licenseJson,
        string? expectedStationCode = null,
        string? expectedFingerprint = null,
        CancellationToken ct = default)
    {
        var publicKeyPem = await _keyCache.GetAsync(_licenseOptions.PublicKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return new LicenseValidateResult(false, "未配置授权公钥", null);

        var result = LicenseEngine.Validate(
            licenseJson,
            publicKeyPem,
            _registry.GetSigner,
            expectedStationCode,
            expectedFingerprint);

        if (result.Valid)
        {
            _logger.LogInformation("授权校验通过：{Key}（到期 {Expires:yyyy-MM-dd}）",
                result.Payload?.LicenseKey, result.Payload?.ExpiresAt);
        }

        return result;
    }

    // =========================================================
    // 授权文件查看（薄壳）
    // =========================================================

    public Task<LicensePayload?> InspectLicenseAsync(
        string licenseJson, CancellationToken ct = default)
    {
        return Task.FromResult(LicenseEngine.Inspect(licenseJson));
    }

    // =========================================================
    // 上报签名（不变）
    // =========================================================

    public async Task<(string? Signature, string? Algorithm)> SignReportingAsync(
        string canonicalJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_reportingOptions.PrivateKeyFile))
            return (null, null);

        var policy = await _policy.GetAsync(CryptoUsage.Reporting, ct).ConfigureAwait(false);
        var signer = await _policy.GetSignerAsync(CryptoUsage.Reporting, ct).ConfigureAwait(false);

        var privateKeyPem = await _keyCache.GetAsync(_reportingOptions.PrivateKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(privateKeyPem)) return (null, null);

        var signature = signer.Sign(Encoding.UTF8.GetBytes(canonicalJson), privateKeyPem);
        return (signature, policy.Algorithm);
    }

    public async Task<bool> VerifyReportingAsync(
        string canonicalJson, string signature, string algorithm, CancellationToken ct = default)
    {
        var publicKeyPem = await _keyCache.GetAsync(_reportingOptions.PublicKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(publicKeyPem)) return false;

        var signer = _registry.GetSigner(algorithm);
        return signer.Verify(Encoding.UTF8.GetBytes(canonicalJson), signature, publicKeyPem);
    }

    // =========================================================
    // 落库加密（不变）
    // =========================================================

    public async Task<string> EncryptLicenseTextAsync(
        string licenseText, CancellationToken ct = default)
    {
        return await _policy.ProtectSecretAsync(
            group: "license",
            key: "payload",
            plaintext: licenseText,
            logger: _logger,
            ct).ConfigureAwait(false);
    }
}
