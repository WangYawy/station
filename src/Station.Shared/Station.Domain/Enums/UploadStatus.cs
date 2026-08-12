namespace Station.Domain.Enums;

/// <summary>文件/任务上传（同步）状态。</summary>
public enum UploadStatus
{
    Pending = 0,
    Uploading = 1,
    Uploaded = 2,
    Failed = 3
}
