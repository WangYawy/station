namespace Station.Domain.Storage;

/// <summary>待上传文件：本地缓存路径 → 远端相对路径。</summary>
public sealed record UploadTargetFile(
    string LocalPath,
    string RemotePath,
    long Size,
    Func<Stream>? LocalStreamFactory = null);

/// <summary>
/// 存储目标抽象：本地磁盘 / FTP / SFTP。
/// 上传成功后按"远端存在且大小一致"判定成功（存储成功标准 2）。
/// </summary>
public interface IStorageTarget
{
    /// <summary>
    /// 存储目标名称
    /// </summary>
    string Name { get; }
    /// <summary>
    /// 上传文件至存储目标
    /// </summary>
    Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken cancellationToken);
    /// <summary>
    /// 获取远端文件大小
    /// </summary>
    Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken);

    /// <summary>计算远端文件 SM3（上传后二次校验）。</summary>
    Task<string> ComputeRemoteSm3Async(string remotePath, CancellationToken cancellationToken);
}
