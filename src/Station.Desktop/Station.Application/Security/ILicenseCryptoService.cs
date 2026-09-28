using Station.Crypto;

namespace Station.Application.Security;

/// <summary>
/// 授权加密服务门面：授权文件生成/验证 + 平台上报签名。
///
/// 【方案说明】授权文件采用"只签名不加密"模式：
///   - 授权内容（站点编号、指纹、到期时间）本来就不是机密；
///   - 签名足以防伪，无需加密；
///   - 采集站只需公钥即可验证，无需与签发方共享主密钥。
/// </summary>
public interface ILicenseCryptoService
{
    #region // ============ 授权文件 ============

    /// <summary>生成授权文件（内部工具用）。</summary>
    Task<string> GenerateLicenseAsync(
        LicensePayload payload, CancellationToken ct = default);

    /// <summary>验证授权文件（验签 + 业务校验）。</summary>
    Task<LicenseValidateResult> ValidateLicenseAsync(
        string licenseJson,
        string? expectedStationCode = null,
        string? expectedFingerprint = null,
        CancellationToken ct = default);

    /// <summary>查看授权内容（不验签，仅解析）。</summary>
    Task<LicensePayload?> InspectLicenseAsync(
        string licenseJson, CancellationToken ct = default);
    #endregion

    #region // ============ 上报签名 ============

    /// <summary>对上报载荷签名（走 reporting 策略）。未配置私钥时返回 (null, null)。</summary>
    Task<(string? Signature, string? Algorithm)> SignReportingAsync(
        string canonicalJson, CancellationToken ct = default);

    /// <summary>验证上报签名。</summary>
    Task<bool> VerifyReportingAsync(
        string canonicalJson, string signature, string algorithm,
        CancellationToken ct = default);
    #endregion

    /// <summary>
    /// 加密授权文件文本（用于落库存储）。
    /// 使用 secret_field 策略，AAD 由 CryptoExtensions 内部派生。
    /// </summary>
    Task<string> EncryptLicenseTextAsync(string licenseText, CancellationToken ct = default);
}
