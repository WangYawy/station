using System.Text;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Application.Licensing;

/// <summary>
/// 授权生成工具（内部使用）。
/// 流程：构造 canonical JSON → SM4-GCM 加密 → SM2-SM3 对密文签名。
/// </summary>
public sealed class LicenseGenerator
{
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoProviderFactory _factory;
    private readonly IKeyFileResolver _keyResolver; // 读 PEM 文件
    private readonly LicenseOptions _options;

    public LicenseGenerator(
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

    public async Task<LicenseFile> GenerateAsync(
        string stationCode, string fingerprint, DateTime expiresAt, CancellationToken ct = default)
    {
        var signPolicy = await _policy.GetAsync(CryptoUsage.License, ct);
        var encryptAlgo = signPolicy.Algorithm;                 // 默认 SM4-GCM
        var signAlgo = signPolicy.SecondaryAlgorithm ?? CryptoAlgorithm.Sm2Sm3;

        var privateKeyPem = await _keyResolver.ResolveAsync(_options.PrivateKeyFile, ct);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException("未配置授权签名私钥文件");

        // 构造明文 payload（不含密文与签名）
        var draft = new LicenseFile(
            $"LIC-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            _options.ProductCode,
            stationCode,
            fingerprint,
            DateTime.Now,
            expiresAt,
            PayloadCipher: string.Empty,
            Algo: $"{encryptAlgo}+{signAlgo}",
            Signature: string.Empty);

        var canonical = LicenseFileCodec.Canonical(draft);
        var cipher = await _factory.TryUnprotectAsync(_policy, "license", "payload", canonical);

        var signer = _factory.GetSigner(signAlgo);
        var signature = signer.Sign(Encoding.UTF8.GetBytes(cipher!), privateKeyPem);

        return draft with { PayloadCipher = cipher!, Signature = signature };
    }

    public async Task<string> GenerateFileTextAsync(
        string stationCode, string fingerprint, DateTime expiresAt, CancellationToken ct = default) =>
        LicenseFileCodec.Serialize(await GenerateAsync(stationCode, fingerprint, expiresAt, ct));
}
