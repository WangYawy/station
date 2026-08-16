using Station.Contracts;
using Station.Domain.Enums;

namespace Station.Application.Collecting;

/// <summary>接入的记录仪信息；识别后带归属（用户/部门）。</summary>
public sealed record CollectDeviceInfo(
    string Name,
    string? Serial,
    ProtocolType Protocol,
    long? UserId = null,
    long? DeptId = null);

public sealed record CollectTaskDto(
    long TaskId,
    string TaskNo,
    string RecorderName,
    string? RecorderSerial,
    Station.Contracts.ProtocolType Protocol,
    long? OperatorUserId,
    long? DeptId,
    CollectTaskStatus Status,
    bool IsAuto,
    int TotalFiles,
    int CollectedFiles,
    int SkippedFiles,
    int FailedFiles,
    long TotalBytes,
    long CollectedBytes,
    double SpeedBytesPerSecond,
    UploadStatus SyncStatus,
    int UploadedFiles,
    long UploadedBytes,
    string? UploadError,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage);

public sealed record CollectFileDto(
    long FileId,
    long TaskId,
    string FileName,
    string Extension,
    long Size,
    CollectFileStatus Status,
    double Progress,
    double SpeedBytesPerSecond,
    string? ErrorMessage,
    DateTime? CollectedAt,
    string? RemotePath,
    string? UploadError);

public static class CollectTaskStatusText
{
    public static string Of(CollectTaskStatus status) => status switch
    {
        CollectTaskStatus.Created => "已创建",
        CollectTaskStatus.Scanning => "扫描中",
        CollectTaskStatus.Collecting => "采集中",
        CollectTaskStatus.Paused => "已暂停",
        CollectTaskStatus.Completed => "已完成",
        CollectTaskStatus.Interrupted => "已中断",
        CollectTaskStatus.Failed => "失败",
        CollectTaskStatus.Canceled => "已取消",
        _ => status.ToString()
    };
}

public static class CollectFileStatusText
{
    public static string Of(CollectFileStatus status) => status switch
    {
        CollectFileStatus.Pending => "待采集",
        CollectFileStatus.Copying => "采集中",
        CollectFileStatus.Verifying => "校验中",
        CollectFileStatus.Completed => "已完成",
        CollectFileStatus.Skipped => "已跳过",
        CollectFileStatus.Failed => "失败",
        CollectFileStatus.Abnormal => "异常",
        CollectFileStatus.Canceled => "已取消",
        _ => status.ToString()
    };
}
