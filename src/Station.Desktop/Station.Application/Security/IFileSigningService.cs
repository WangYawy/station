namespace Station.Application.Security;

/// <summary>
/// 文件摘要与元数据签名服务。
/// 
/// 【职责】
///   1) 计算文件明文内容摘要（走 file_sig 策略，默认 SM3）；
///   2) 对元数据 JSON 签名（走 file_sig 策略的 SecondaryAlgorithm，默认 SM2-SM3）；
///   3) 验证元数据签名（平台端用）。
/// 
/// 【签名语义】
///   签名对象是"规范化 JSON 元数据包"，不是文件本身。
///   元数据包含 ContentDigest，等价于对文件内容签名。
/// </summary>
public interface IFileSigningService
{
    /// <summary>算文件明文内容摘要。</summary>
    Task<(string Digest, string Algorithm)> ComputeDigestAsync(
        string filePath, CancellationToken ct = default);

    /// <summary>对元数据 JSON 签名。</summary>
    Task<(string Signature, string Algorithm)> SignMetadataAsync(
        string metadataJson, CancellationToken ct = default);

    /// <summary>验证元数据签名。</summary>
    Task<bool> VerifyMetadataAsync(
        string metadataJson, string signature, string signAlgorithm,
        CancellationToken ct = default);
}
