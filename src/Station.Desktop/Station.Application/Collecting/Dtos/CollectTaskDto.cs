using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Application.Collecting.Dtos;

/// <summary>
/// 采集任务 DTO（读模型）。
/// </summary>
public sealed record CollectTaskDto(
    long TaskId,
    string TaskNo,
    string? DeviceKey,

    long RecorderId,
    string RecorderCode,
    string RecorderName,

    string? RecorderSerial,
    ProtocolType Protocol,
    string? PhysicalPortId,

    long? OperatorUserId,
    string? OperatorUserName,
    string? OperatorDeptName,
    long? DeptId,
    string? DeptName,
    CollectTaskStatus Status,
    bool IsAuto,
    int TotalFiles,
    int CollectedFiles,
    int SkippedFiles,
    int FailedFiles,
    long TotalBytes,
    long CollectedBytes,
    double SpeedBytesPerSecond,
    bool IsEmergency,
    UploadStatus SyncStatus,
    int UploadedFiles,
    long UploadedBytes,
    string? UploadError,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    DateTime CreateAt);           // 新增，末尾默认值
