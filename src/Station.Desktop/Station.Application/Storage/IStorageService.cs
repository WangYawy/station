
namespace Station.Application.Storage;

/// <summary>存储服务：把本地文件上传到一个或多个存储目标，聚合结果。</summary>
public interface IStorageService
{
    Task<FileUploadResult> UploadFileAsync(
        string localPath,
        string remoteDirectory,
        string fileName,
        long fileSize,
        string? expectedLocal,
        Func<Stream>? localStreamFactory = null,
        CancellationToken cancellationToken = default);
}
