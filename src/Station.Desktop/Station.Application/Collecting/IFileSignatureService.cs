namespace Station.Application.Collecting;

/// <summary>采集文件签名服务（本地签名，上传后沿用同摘要）。</summary>
public interface IFileSignatureService
{
    /// <summary>
    /// 对文件计算摘要并签名，返回 (摘要 Base64, 算法标识, 签名 Base64)。
    /// 摘要算法由 file_sig 策略决定（默认 SM3）。
    /// </summary>
    Task<(string Digest, string DigestAlgo, string? Signature)> SignFileAsync(
        string filePath, CancellationToken ct = default);

    /// <summary>校验文件摘要。</summary>
    Task<bool> VerifyFileAsync(string filePath, string expectedDigest,
        string digestAlgo, CancellationToken ct = default);
}
