namespace Station.Infrastructure.Storage;

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
    string Name { get; }

    Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken cancellationToken);

    Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken);

    /// <summary>计算远端文件 SM3（上传后二次校验）。</summary>
    Task<string> ComputeRemoteSm3Async(string remotePath, CancellationToken cancellationToken);
}
