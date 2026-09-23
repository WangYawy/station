namespace Station.Application.Storage;

/// <summary>
/// 上传任务的输入描述。
/// 
/// LocalStreamFactory 契约（重要）：
///   - 每次调用必须返回一个"全新的、支持 Seek 的、可独立读取的"流；
///   - 多 target 并行上传时会各自调用一次，禁止共享同一流实例；
///   - 返回 null 时，实现方将退化为 File.OpenRead(LocalPath)。
/// </summary>
public sealed record UploadTargetFile(
    string LocalPath,
    string RemotePath,
    long Size,
    Func<Stream>? LocalStreamFactory,
    string? ExpectedDigest = null,
    string? DigestAlgorithm = null)
{
    /// <summary>获取一个新的本地流。委托为 null 时使用文件系统流。</summary>
    public Stream OpenRead() => LocalStreamFactory?.Invoke() ?? File.OpenRead(LocalPath);
}

/// <summary>
/// 存储目标抽象：本地磁盘 / FTP / SFTP 的统一接口。
/// 所有实现必须无状态（连接池在实现内部维护），Name 必须唯一（用于日志/熔断/结果聚合）。
/// </summary>
public interface IStorageTarget
{
    /// <summary>目标唯一名称（例如 "local-1" / "ftp-backup" / "sftp-archive"）。</summary>
    string Name { get; }

    /// <summary>目标类型。用于 UI 展示与日志分类。</summary>
    StorageTargetKind Kind { get; }

    /// <summary>
    /// 上传文件。onProgress 回调范围 [0,1]，实现方必须保证单次调用内单调递增。
    /// 实现方必须支持断点续传（远端已有部分时从断点续传），并保证幂等（远端已完整则跳过）。
    /// </summary>
    Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken ct);

    /// <summary>获取远端文件大小（字节）。文件不存在应抛 FileNotFoundException。</summary>
    Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken ct);

    /// <summary>
    /// 计算远端文件摘要（算法由 file_sig 策略决定，默认 SM3）。
    /// 返回 (Base64 摘要, 算法标识)。
    /// </summary>
    Task<RemoteDigestResult> ComputeRemoteDigestAsync(string remotePath, CancellationToken ct);

    /// <summary>
    /// 对远端文件签名：下载远端文件 → 本地算摘要 → 本地私钥签名。
    /// 
    /// 说明：
    ///   - FTP/SFTP 不支持远端签名，所以必须在客户端完成；
    ///   - 大文件建议搭配 RemoteVerifyMode=None（只在本地签名，不重新下载）；
    ///   - 若只需摘要（无签名），用 ComputeRemoteDigestAsync 即可。
    /// </summary>
    Task<RemoteSignatureResult> SignRemoteAsync(
        string remotePath, string privateKeyPem, CancellationToken ct);

    /// <summary>
    /// 删除远端文件（尽力而为）。文件不存在时静默成功。
    /// 用途：取消/失败后的清理，或业务删除。
    /// </summary>
    Task DeleteAsync(string remotePath, CancellationToken ct);
}

/// <summary>远端摘要结果。</summary>
public sealed record RemoteDigestResult(string Digest, string Algorithm);

/// <summary>远端签名结果。</summary>
public sealed record RemoteSignatureResult(
    string Digest,
    string DigestAlgorithm,
    string Signature,
    string SignAlgorithm);

/// <summary>熔断器状态。</summary>
public enum StorageCircuitState
{
    /// <summary>正常通行。</summary>
    Closed = 0,

    /// <summary>已熔断，拒绝所有请求。</summary>
    Open = 1,

    /// <summary>冷却结束，只放行单个探测请求。</summary>
    HalfOpen = 2
}

public enum StorageTargetKind
{
    Local = 0,
    Ftp = 1,
    Sftp = 2
}
