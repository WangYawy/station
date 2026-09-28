namespace Station.Crypto;

/// <summary>
/// 主密钥提供者抽象。
///
/// 【单机版】Station.Crypto.Engine.Keys.FileMasterKeyProvider（读本地 JSON 文件）
/// 【集群版】Station.Infrastructure.Security.Keys.RemoteMasterKeyProvider（平台下发 + 本地缓存）
/// 【Tools】  只用 FileMasterKeyProvider
///
/// ⚠️ 实现必须线程安全。
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>当前主密钥版本号（用于新数据加密）。</summary>
    int CurrentVersion { get; }

    /// <summary>返回所有已知版本号（升序）。</summary>
    IReadOnlyList<int> GetVersions();

    /// <summary>
    /// 按版本取 32 字节主密钥。
    /// </summary>
    /// <exception cref="MasterKeyNotFoundException">版本不存在。</exception>
    byte[] GetKey(int version);

    /// <summary>生成新版本密钥并设为当前版本，返回新版本号。</summary>
    Task<int> RotateAsync(CancellationToken ct = default);

    /// <summary>
    /// 派生 Binding MAC 密钥。
    /// 约定：HKDF-SM3(GetKey(CurrentVersion), HkdfInfo.RecorderBinding, 32)。
    /// </summary>
    byte[] DeriveBindingKey();

    /// <summary>
    /// 派生文件加密密钥。
    /// 约定：HKDF-SM3(GetKey(keyVersion), HkdfInfo.FileEncryption, 32)。
    /// 注意：需要按文件头部记录的 keyVersion 派生，而不是 CurrentVersion。
    /// </summary>
    byte[] DeriveFileEncryptionKey(int keyVersion);
}
