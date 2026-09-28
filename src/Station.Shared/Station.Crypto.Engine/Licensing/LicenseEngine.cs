using System.Text;

namespace Station.Crypto.Engine.Licensing;

/// <summary>
/// 授权文件引擎（纯编排，无 DI，无日志，无文件 IO）。
///
/// 【职责】payload + 私钥 + signer → 完整授权文件；反之验签 + 业务校验。
/// 【不负责】策略解析、密钥读取、审计、日志 —— 这些由调用方处理。
///
/// 【调用方】
///   - Tools.LicenseBuilder：传 AlgorithmRegistry.Default.GetSigner
///   - Infrastructure.LicenseCryptoService：传 ICryptoPolicyService 解析出的 signer
/// </summary>
public static class LicenseEngine
{
    /// <summary>
    /// 生成授权文件。
    /// </summary>
    /// <param name="payload">授权 payload（含 LicenseKey 等元数据）。</param>
    /// <param name="privateKeyPem">签名私钥 PEM。</param>
    /// <param name="signer">已解析的签名器实例。</param>
    /// <returns>序列化后的授权文件 JSON。</returns>
    public static string Build(
        LicensePayload payload,
        string privateKeyPem,
        ISigner signer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        ArgumentNullException.ThrowIfNull(signer);

        var canonical = LicenseFileCodec.CanonicalPayload(payload);
        var signature = signer.Sign(Encoding.UTF8.GetBytes(canonical), privateKeyPem);

        var file = new LicenseFile(
            payload.LicenseKey, payload.ProductCode, payload.StationCode,
            payload.Fingerprint, payload.IssuedAt, payload.ExpiresAt,
            canonical, signer.Algorithm, signature);

        return LicenseFileCodec.Serialize(file);
    }

    /// <summary>
    /// 验证授权文件（验签 + 业务校验）。
    /// </summary>
    /// <param name="licenseJson">授权文件 JSON。</param>
    /// <param name="publicKeyPem">验证公钥 PEM。</param>
    /// <param name="signerResolver">
    /// 按算法标识解析 signer 的委托。
    /// 从文件内 SignatureAlgorithm 字段读出算法，再交由调用方解析。
    /// </param>
    /// <param name="expectedStationCode">期望的站点编号（null 表示不校验）。</param>
    /// <param name="expectedFingerprint">期望的硬件指纹（null 表示不校验）。</param>
    /// <param name="now">当前时间（用于测试注入；null 表示 DateTime.UtcNow）。</param>
    public static LicenseValidateResult Validate(
        string licenseJson,
        string publicKeyPem,
        Func<string, ISigner> signerResolver,
        string? expectedStationCode = null,
        string? expectedFingerprint = null,
        DateTime? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        ArgumentNullException.ThrowIfNull(signerResolver);

        // 1) 解析文件结构
        LicenseFile file;
        try
        {
            file = LicenseFileCodec.Parse(licenseJson);
        }
        catch (Exception ex)
        {
            return new LicenseValidateResult(false, $"授权文件无效：{ex.Message}", null);
        }

        // 2) 按文件内 SignatureAlgorithm 解析 signer
        ISigner signer;
        try
        {
            signer = signerResolver(file.SignatureAlgorithm);
        }
        catch (NotSupportedException ex)
        {
            return new LicenseValidateResult(false, $"不支持的签名算法：{ex.Message}", null);
        }

        // 3) 验签
        if (!signer.Verify(
                Encoding.UTF8.GetBytes(file.Payload),
                file.Signature,
                publicKeyPem))
        {
            return new LicenseValidateResult(false, "签名无效", null);
        }

        // 4) 解析 payload
        LicensePayload payload;
        try
        {
            payload = LicenseFileCodec.ParsePayload(file.Payload);
        }
        catch (Exception ex)
        {
            return new LicenseValidateResult(false, $"授权内容解析失败：{ex.Message}", null);
        }

        // 5) 业务校验
        if (!string.IsNullOrEmpty(expectedStationCode)
            && !string.Equals(payload.StationCode, expectedStationCode, StringComparison.OrdinalIgnoreCase))
        {
            return new LicenseValidateResult(false, "站点编号不匹配", payload);
        }

        if (!string.IsNullOrEmpty(expectedFingerprint)
            && !string.Equals(payload.Fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new LicenseValidateResult(false, "硬件指纹不匹配", payload);
        }

        var utcNow = now ?? DateTime.UtcNow;
        if (payload.ExpiresAt < utcNow)
        {
            return new LicenseValidateResult(false, "授权已过期", payload);
        }

        return new LicenseValidateResult(true, "授权有效", payload);
    }

    /// <summary>
    /// 仅查看授权内容（不验签）。
    /// </summary>
    /// <returns>解析成功返回 payload；解析失败返回 null。</returns>
    public static LicensePayload? Inspect(string licenseJson)
    {
        if (string.IsNullOrWhiteSpace(licenseJson)) return null;

        try
        {
            var file = LicenseFileCodec.Parse(licenseJson);
            return LicenseFileCodec.ParsePayload(file.Payload);
        }
        catch
        {
            return null;
        }
    }
}
