namespace Station.Application.Security;

/// <summary>
/// 上报数据签名门面：对上报给平台的 JSON 载荷签名。
/// 内部走 CryptoUsage.Reporting 策略，私钥从 ReportingOptions.PrivateKeyFile 读。
/// </summary>
public interface IReportingSigner
{
    /// <summary>
    /// 对规范化 JSON 载荷签名。
    /// </summary>
    /// <returns>(签名 Base64, 签名算法标识)。未配置私钥时返回 (null, null)。</returns>
    Task<(string? Signature, string? Algorithm)> SignAsync(
        string canonicalJson, CancellationToken ct = default);

    /// <summary>验证签名（平台端用）。</summary>
    Task<bool> VerifyAsync(
        string canonicalJson, string signature, string algorithm,
        CancellationToken ct = default);
}
