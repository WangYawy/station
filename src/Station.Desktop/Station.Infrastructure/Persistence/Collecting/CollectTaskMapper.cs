using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

internal static class CollectTaskMapper
{
    public static CollectTask ToDomain(CollectTaskPo po)
        => CollectTask.Restore(
            po.Id, po.TaskNo,
            po.RecorderName, po.RecorderSerial, po.Protocol, po.SourceRoot,
            po.OperatorUserId, po.DeptId,
            po.Status, po.IsAuto, po.IsEmergency,
            po.TotalFiles, po.CollectedFiles, po.SkippedFiles, po.FailedFiles,
            po.TotalBytes, po.CollectedBytes, po.SpeedBytesPerSecond,
            po.StartedAt, po.CompletedAt, po.ErrorMessage,
            po.SyncStatus, po.UploadedFiles, po.UploadedBytes, po.UploadError,
            po.LedgeredAt, po.CreatedAt);

    public static CollectTaskPo ToPo(CollectTask t)
        => new()
        {
            Id = t.Id,
            TaskNo = t.TaskNo,
            RecorderName = t.RecorderName,
            RecorderSerial = t.RecorderSerial,
            Protocol = t.Protocol,
            SourceRoot = t.SourceRoot,
            OperatorUserId = t.OperatorUserId,
            DeptId = t.DeptId,
            Status = t.Status,
            IsAuto = t.IsAuto,
            IsEmergency = t.IsEmergency,
            TotalFiles = t.TotalFiles,
            CollectedFiles = t.CollectedFiles,
            SkippedFiles = t.SkippedFiles,
            FailedFiles = t.FailedFiles,
            TotalBytes = t.TotalBytes,
            CollectedBytes = t.CollectedBytes,
            SpeedBytesPerSecond = t.SpeedBytesPerSecond,
            StartedAt = t.StartedAt,
            CompletedAt = t.CompletedAt,
            ErrorMessage = t.ErrorMessage,
            SyncStatus = t.SyncStatus,
            UploadedFiles = t.UploadedFiles,
            UploadedBytes = t.UploadedBytes,
            UploadError = t.UploadError,
            LedgeredAt = t.LedgeredAt,
            CreatedAt = t.CreatedAt
        };
}
