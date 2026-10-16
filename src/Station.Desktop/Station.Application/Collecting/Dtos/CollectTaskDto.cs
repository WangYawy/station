using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Application.Collecting.Dtos;

public sealed record CollectTaskDto(
    long TaskId,
    string TaskNo,
    string RecorderName,
    string? RecorderSerial,
    ProtocolType Protocol,
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
    string? ErrorMessage);
