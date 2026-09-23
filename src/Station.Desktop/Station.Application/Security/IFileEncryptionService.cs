namespace Station.Application.Security;

/// <summary>
/// 大文件加密服务（分块 SM4-GCM / AES-256-GCM）。
/// 
/// 【文件格式 STFE】
///   Header(32B) + Chunk0 + Chunk1 + ...
///   Header: Magic(4) | Version(1) | Algo(1) | KeyVersion(4) | ChunkSize(4) | NonceSeed(8) | Reserved(10)
///   Chunk:  Nonce(12) | Ciphertext(N) | Tag(16)
/// 
/// 【密钥来源】
///   主密钥（IMasterKeyProvider）→ HKDF-SM3 派生 → 文件加密密钥（32B）
///   HKDF info = "station:file-encryption:v1"
/// 
/// 【流式处理】
///   单块 ChunkSize（默认 4MB）读入内存，加/解密后写盘，内存峰值可控。
/// </summary>
public interface IFileEncryptionService
{
    /// <summary>
    /// 加密文件：源路径 → 目标路径（流式，分块）。
    /// </summary>
    /// <returns>密文字节数（含 Header + 分块开销）。</returns>
    Task<long> EncryptAsync(
        string sourcePath, string targetPath, CancellationToken ct = default);

    /// <summary>
    /// 原地加密：内部用临时文件 + 原子替换。
    /// </summary>
    /// <returns>密文字节数。</returns>
    Task<long> EncryptInPlaceAsync(
        string filePath, CancellationToken ct = default);

    /// <summary>
    /// 打开解密流（顺序读，不支持 Seek）。
    /// </summary>
    Stream CreateDecryptStream(string encryptedFilePath);

    /// <summary>判断文件是否已加密（读头部 Magic）。</summary>
    bool IsEncrypted(string filePath);

    /// <summary>读取头部信息（用于快速探测）。</summary>
    FileEncryptionHeader? TryReadHeader(string filePath);
}

/// <summary>STFE 文件头信息。</summary>
public sealed record FileEncryptionHeader(
    byte Version,
    string Algorithm,
    int KeyVersion,
    int ChunkSize,
    long TotalCiphertextSize);
