namespace Station.Application.Uploading;

/// <summary>
/// 上传/同步服务：采集完成文件 → 存储目标（本地/FTP/SFTP），
/// 断点续传/分片、远端校验、失败重试、存储熔断。
/// </summary>
public interface IUploadService
{
    /// <summary>处理指定任务的全部待上传文件，返回处理后结果摘要。</summary>
    Task<UploadSummary> ProcessTaskAsync(long taskId);

    /// <summary>重试任务中已失败的上传文件。</summary>
    Task<int> RetryFailedFilesAsync(long taskId);

    /// <summary>待上传文件数（工作台统计）。</summary>
    Task<int> CountPendingUploadsAsync();
}

public sealed record UploadSummary(long TaskId, int Uploaded, int Failed, int Pending, bool CircuitOpen);
