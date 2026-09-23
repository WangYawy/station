using System.Text;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Application.Licensing;

/// <summary>
/// 授权文件校验：
/// 1) 解析授权文件；
/// 2) 校验签名（对密文，或旧格式对明文）；
/// 3) 解密 payload（新格式）；
/// 4) 校验站点/指纹/到期。
/// </summary>
public sealed class LicenseValidator
{
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoProviderFactory _factory;
    private readonly IKeyFileResolver _keyResolver;
    private readonly LicenseOptions _options;

    public LicenseValidator(
        ICryptoPolicyService policy,
        ICryptoProviderFactory factory,
        IKeyFileResolver keyResolver,
        LicenseOptions options)
    {
        _policy = policy;
        _factory = factory;
        _keyResolver = keyResolver;
        _options = options;
    }

    public async Task<(bool Ok, string Message, LicenseFile? File)> ValidateAsync(
        string licenseText, string expectedStationCode, string expectedFingerprint, CancellationToken ct = default)
    {
        LicenseFile file;
        try { file = LicenseFileCodec.Parse(licenseText); }
        catch { return (false, "授权文件格式错误", null); }

        var publicKeyPem = await _keyResolver.ResolveAsync(_options.PublicKeyFile, ct);
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return (false, "未配置授权验签公钥", null);

        var policy = await _policy.GetAsync(CryptoUsage.License, ct);
        var signAlgo = policy.SecondaryAlgorithm ?? CryptoAlgorithm.Sm2Sm3;
        var signer = _factory.GetSigner(signAlgo);

        byte[] dataToVerify;
        LicenseFile? payload = null;

        dataToVerify = Encoding.UTF8.GetBytes(file.PayloadCipher);

        if (!signer.Verify(dataToVerify, file.Signature, publicKeyPem))
            return (false, "授权签名校验失败", null);

        var encryptor = _factory.GetEncryptor(policy.Algorithm);
        try
        {
            var plain = encryptor.Decrypt(file.PayloadCipher, "license.payload");
            payload = LicenseFileCodec.Parse(plain);
        }
        catch (Exception ex)
        {
            return (false, $"授权解密失败：{ex.Message}", null);
        }

        if (payload is null) return (false, "授权内容为空", null);
        if (!string.Equals(payload.StationCode, expectedStationCode, StringComparison.OrdinalIgnoreCase))
            return (false, "授权站点编号不匹配", null);
        if (!string.Equals(payload.Fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            return (false, "授权硬件指纹不匹配", null);
        if (payload.ExpiresAt < DateTime.Now)
            return (false, "授权已过期", payload);

        return (true, "授权有效", payload);
    }
}
