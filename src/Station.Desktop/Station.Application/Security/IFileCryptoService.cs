using Station.Crypto.Formats;

namespace Station.Application.Security;

/// <summary>
/// 文件加密服务门面：摘要 + 元数据签名 + STFE 文件加解密。
/// 
/// 【合并说明】原 IFileEncryptionService + IFileSigningService 合并。
/// </summary>
public interface IFileCryptoService
{
    /// <summary>
    /// 获取当前 file_sig 策略的摘要算法标识（不实际计算摘要）。
    /// 
    /// 【用途】调用方已算好摘要（如采集阶段），只需知道"用的是什么算法"时使用。
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

    /// <summary>打开解密流（顺序读，不支持 Seek）。</summary>
    Stream CreateDecryptStream(string encryptedFilePath);

    /// <summary>判断文件是否已加密（STFE 格式）。</summary>
    bool IsEncrypted(string filePath);

    /// <summary>读取 STFE 文件头。</summary>
    StfeHeader? TryReadHeader(string filePath);
}

/// <summary>
/// 文件元数据（用于签名）。
/// </summary>
public sealed record FileMetadata(
    string FileNo,
    string FileName,
    long Size,
    string ContentDigest,
    string DigestAlgorithm,
    DateTime CollectedAt,
    string StationCode);

/// <summary>元数据规范化 JSON 序列化器。</summary>
public static class FileMetadataCodec
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    /// <summary>生成规范化 JSON（用于签名）。</summary>
    public static string Canonical(FileMetadata meta) =>
        System.Text.Json.JsonSerializer.Serialize(meta, Options);

    /// <summary>反序列化。</summary>
    public static FileMetadata Parse(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<FileMetadata>(json, Options)
        ?? throw new InvalidOperationException("元数据 JSON 反序列化失败");
}
