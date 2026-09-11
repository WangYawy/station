namespace Station.Application.Storage;

/// <summary>
/// 存储服务
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// 上传单个文件到所有配置的存储目标，内部包含重试、校验和熔断状态。
    /// </summary>
    Task<FileUploadResult> UploadFileAsync(
        string localPath,
        string remoteDirectory,
        string fileName,
        long fileSize,
        string? expectedLocalSm3,
        Func<Stream>? localStreamFactory = null,
        CancellationToken cancellationToken = default);
}
