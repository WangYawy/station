namespace Station.Application.Security;

/// <summary>
/// 授权加密服务门面：授权文件生成/验证 + 平台上报签名。
/// </summary>
public interface ILicenseCryptoService
{
    // ============ 授权文件 ============

    /// <summary>生成授权文件（内部工具用）。</summary>
    Task<string> GenerateLicenseAsync(
        LicensePayload payload, CancellationToken ct = default);

    /// <summary>验证授权文件（验签 + 解密 + 业务校验）。</summary>
    Task<LicenseValidateResult> ValidateLicenseAsync(
        string licenseJson,
        string? expectedStationCode = null,
        string? expectedFingerprint = null,
        CancellationToken ct = default);

    /// <summary>查看授权内容（仅解密，不验签）。</summary>
    Task<LicensePayload?> InspectLicenseAsync(
        string licenseJson, CancellationToken ct = default);

    // ============ 上报签名 ============

    /// <summary>对上报载荷签名（走 reporting 策略）。未配置私钥时返回 (null, null)。</summary>
    Task<(string? Signature, string? Algorithm)> SignReportingAsync(
        string canonicalJson, CancellationToken ct = default);

    /// <summary>验证上报签名。</summary>
    Task<bool> VerifyReportingAsync(
        string canonicalJson, string signature, string algorithm,
        CancellationToken ct = default);

    /// <summary>
    /// 加密授权文件文本（用于落库存储）。
    /// 使用 secret_field 策略，AAD 由 CryptoExtensions 内部派生。
    /// </summary>
    Task<string> EncryptLicenseTextAsync(string licenseText, CancellationToken ct = default);
}

// =============================================================
// 授权文件模型
// =============================================================

/// <summary>授权 payload（canonical 结构）。</summary>
public sealed record LicensePayload(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt);

/// <summary>完整授权文件。</summary>
public sealed record LicenseFile(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string PayloadCipher,
    string Algo,
    string Signature);

/// <summary>授权验证结果。</summary>
public sealed record LicenseValidateResult(
    bool Valid,
    string Message,
    LicensePayload? Payload);

/// <summary>授权文件序列化辅助。</summary>
public static class LicenseFileCodec
{
    private static readonly System.Text.Json.JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private static readonly System.Text.Json.JsonSerializerOptions DisplayOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    /// <summary>payload → canonical JSON。</summary>
    public static string CanonicalPayload(LicensePayload payload) =>
        System.Text.Json.JsonSerializer.Serialize(payload, CanonicalOpts);

    /// <summary>完整授权文件序列化。</summary>
    public static string Serialize(LicenseFile file) =>
        System.Text.Json.JsonSerializer.Serialize(file, DisplayOpts);

    /// <summary>完整授权文件反序列化。</summary>
    public static LicenseFile Parse(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<LicenseFile>(json, DisplayOpts)
        ?? throw new InvalidOperationException("授权文件解析失败");

    /// <summary>payload 反序列化。</summary>
    public static LicensePayload ParsePayload(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<LicensePayload>(json, CanonicalOpts)
        ?? throw new InvalidOperationException("授权 payload 解析失败");
}
