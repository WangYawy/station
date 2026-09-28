using Station.Crypto;
using Station.Crypto.Formats;

namespace Station.Application.Security;

/// <summary>
/// 文件加密服务门面：摘要 + 元数据签名 + STFE 文件加解密。
/// </summary>
public interface IFileCryptoService
{
    /// <summary>
    /// 获取当前 file_sig 策略的摘要算法标识（不实际计算摘要）。
    /// </summary>
    Task<string> GetDigestAlgorithmAsync(CancellationToken ct = default);

    // ============ 摘要 ============

    /// <summary>计算文件明文摘要（按 file_sig 策略）。</summary>
    Task<(string Digest, string Algorithm)> ComputeDigestAsync(
        string filePath, CancellationToken ct = default);

    // ============ 元数据签名 ============

    /// <summary>对文件元数据签名（按 file_sig 策略 SecondaryAlgorithm）。</summary>
    Task<(string Signature, string Algorithm)> SignMetadataAsync(
        FileMetadata metadata, CancellationToken ct = default);

    /// <summary>验证元数据签名。</summary>
    Task<bool> VerifyMetadataAsync(
        FileMetadata metadata, string signature, string algorithm,
        CancellationToken ct = default);

    // ============ STFE 文件加密 ============

    /// <summary>加密文件（源 → 目标）。返回密文总字节数。</summary>
    Task<long> EncryptAsync(string sourcePath, string targetPath, CancellationToken ct = default);

    /// <summary>原地加密（用临时文件 + 原子替换）。返回密文总字节数。</summary>
    Task<long> EncryptInPlaceAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// 打开流式解密（顺序读，不支持 Seek，内存 O(chunkSize)）。
    /// </summary>
    Stream OpenDecryptStream(string encryptedFilePath);

    /// <summary>
    /// 异步分块解密（上传友好），每块 80KB。
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> DecryptChunksAsync(
        string encryptedFilePath, CancellationToken ct = default);

    /// <summary>判断文件是否已加密（STFE 格式）。</summary>
    bool IsEncrypted(string filePath);

    /// <summary>读取 STFE 文件头。</summary>
    StfeHeader? TryReadHeader(string filePath);
}
