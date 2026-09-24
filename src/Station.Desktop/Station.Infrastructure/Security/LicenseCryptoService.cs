using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Domain.Security;
using Station.Infrastructure.Security.Internal;
using Station.Infrastructure.Security.Keys;

namespace Station.Infrastructure.Security;

/// <summary>授权加密服务实现。</summary>
public sealed class LicenseCryptoService : ILicenseCryptoService
{
    private readonly ICryptoPolicyService _policy;
    private readonly MasterKeyProvider _keys;
    private readonly PemKeyCache _keyCache;
    private readonly LicenseOptions _licenseOptions;
    private readonly ReportingOptions _reportingOptions;
    private readonly ILogger<LicenseCryptoService> _logger;

    public LicenseCryptoService(
        ICryptoPolicyService policy,
        MasterKeyProvider keys,
        PemKeyCache keyCache,
        IOptions<LicenseOptions> licenseOptions,
        IOptions<ReportingOptions> reportingOptions,
        ILogger<LicenseCryptoService> logger)
    {
        _policy = policy;
        _keys = keys;
        _keyCache = keyCache;
        _licenseOptions = licenseOptions.Value;
        _reportingOptions = reportingOptions.Value;
        _logger = logger;
    }

    // =========================================================
    // 授权文件生成
    // =========================================================

    public async Task<string> GenerateLicenseAsync(
        LicensePayload payload, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.License, ct).ConfigureAwait(false);
        var encryptor = await _policy.GetEncryptorAsync(CryptoUsage.License, ct).ConfigureAwait(false);
        var signer = await _policy.GetSignerAsync(CryptoUsage.License, ct).ConfigureAwait(false);

        var privateKeyPem = await _keyCache.GetAsync(_licenseOptions.PrivateKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException(
                $"未配置授权签名私钥：{_licenseOptions.PrivateKeyFile}");

        // 1) canonical → 2) SM4 加密 → 3) SM2 签名
        var canonical = LicenseFileCodec.CanonicalPayload(payload);
        var cipher = encryptor.Encrypt(canonical, "license.payload");
        var signAlgo = policy.SecondaryAlgorithm ?? policy.Algorithm;
        var signature = signer.Sign(Encoding.UTF8.GetBytes(cipher), privateKeyPem);

        var file = new LicenseFile(
            payload.LicenseKey, payload.ProductCode, payload.StationCode,
            payload.Fingerprint, payload.IssuedAt, payload.ExpiresAt,
            cipher, $"{policy.Algorithm}+{signAlgo}", signature);

        return LicenseFileCodec.Serialize(file);
    }

    // =========================================================
    // 授权文件验证
    // =========================================================

    public async Task<LicenseValidateResult> ValidateLicenseAsync(
        string licenseJson,
        string? expectedStationCode = null,
        string? expectedFingerprint = null,
        CancellationToken ct = default)
    {
        LicenseFile file;
        try { file = LicenseFileCodec.Parse(licenseJson); }
        catch (Exception ex) { return new LicenseValidateResult(false, $"授权文件无效：{ex.Message}", null); }

        // 1) 验签
        var publicKeyPem = await _keyCache.GetAsync(_licenseOptions.PublicKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return new LicenseValidateResult(false, "未配置授权公钥", null);

        var (encryptAlgo, signAlgo) = ParseAlgoTag(
            file.Algo, _licenseOptions.EncryptAlgorithm, _licenseOptions.SignAlgorithm);

        var signer = AlgorithmResolver.ResolveSigner(signAlgo);
        if (!signer.Verify(Encoding.UTF8.GetBytes(file.PayloadCipher), file.Signature, publicKeyPem))
            return new LicenseValidateResult(false, "授权签名无效", null);

        // 2) 解密
        LicensePayload payload;
        try
        {
            var encryptor = await _policy.GetEncryptorAsync(CryptoUsage.License, ct)
                .ConfigureAwait(false);
            var plain = encryptor.Decrypt(file.PayloadCipher, "license.payload");
            payload = LicenseFileCodec.ParsePayload(plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "授权解密失败");
            return new LicenseValidateResult(false, "授权解密失败", null);
        }

        // 3) 业务校验
        if (!string.IsNullOrEmpty(expectedStationCode)
            && !string.Equals(payload.StationCode, expectedStationCode, StringComparison.OrdinalIgnoreCase))
            return new LicenseValidateResult(false, "站点编号不匹配", payload);

        if (!string.IsNullOrEmpty(expectedFingerprint)
            && !string.Equals(payload.Fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            return new LicenseValidateResult(false, "硬件指纹不匹配", payload);

        if (payload.ExpiresAt < DateTime.UtcNow)
            return new LicenseValidateResult(false, "授权已过期", payload);

        return new LicenseValidateResult(true, "授权有效", payload);
    }

    public async Task<LicensePayload?> InspectLicenseAsync(
        string licenseJson, CancellationToken ct = default)
    {
        try
        {
            var file = LicenseFileCodec.Parse(licenseJson);
            var encryptor = await _policy.GetEncryptorAsync(CryptoUsage.License, ct)
                .ConfigureAwait(false);
            var plain = encryptor.Decrypt(file.PayloadCipher, "license.payload");
            return LicenseFileCodec.ParsePayload(plain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "授权查看失败");
            return null;
        }
    }

    // =========================================================
    // 上报签名
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

        var signer = AlgorithmResolver.ResolveSigner(algorithm);
        return signer.Verify(Encoding.UTF8.GetBytes(canonicalJson), signature, publicKeyPem);
    }
    public async Task<string> EncryptLicenseTextAsync(string licenseText, CancellationToken ct = default)
    {
        return await _policy.ProtectSecretAsync(
            group: "license",
            key: "payload",
            plaintext: licenseText,
            logger: _logger,
            ct).ConfigureAwait(false);
    }
    // =========================================================
    // 内部
    // =========================================================

    private static (string Encrypt, string Sign) ParseAlgoTag(
        string? tag, string fallbackEncrypt, string fallbackSign)
    {
        if (string.IsNullOrWhiteSpace(tag)) return (fallbackEncrypt, fallbackSign);

        var idx = tag.IndexOf('+');
        if (idx < 0) return (tag.Trim(), fallbackSign);
        return (tag[..idx].Trim(), tag[(idx + 1)..].Trim());
    }
}
