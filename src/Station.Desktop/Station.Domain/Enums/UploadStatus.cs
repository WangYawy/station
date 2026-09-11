namespace Station.Domain.Enums;

/// <summary>文件/任务上传（同步）状态。</summary>
public enum UploadStatus
{
    /// <summary>
    /// 待处理
    /// </summary>
    Pending = 0,
    /// <summary>
    /// 上传中
    /// </summary>
    Uploading = 1,
    /// <summary>
    /// 上传完成
    /// </summary>
    Uploaded = 2,
    /// <summary>
    /// 上传失败
    /// </summary>
    Failed = 3
}
