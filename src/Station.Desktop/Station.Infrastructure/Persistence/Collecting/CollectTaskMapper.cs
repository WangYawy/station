using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

/// <summary>CollectTask ⇄ CollectTaskPo 双向映射。</summary>
public static class CollectTaskMapper
{
    public static CollectTask ToDomain(CollectTaskPo po)
    {
        var deviceRef = BuildDeviceRef(po);

        return CollectTask.Restore(
            id: po.Id,
            taskNo: po.TaskNo,
            recorderId: po.RecorderId,
            recorderCode: po.RecorderCode,
            device: deviceRef,
            operatorUserId: po.OperatorUserId,
            deptId: po.DeptId,
            status: po.Status,
            isAuto: po.IsAuto,
            isEmergency: po.IsEmergency,
            totalFiles: po.TotalFiles,
            collectedFiles: po.CollectedFiles,
            skippedFiles: po.SkippedFiles,
            failedFiles: po.FailedFiles,
            totalBytes: po.TotalBytes,
            collectedBytes: po.CollectedBytes,
            speedBytesPerSecond: po.SpeedBytesPerSecond,
            syncStatus: po.SyncStatus,
            uploadedFiles: po.UploadedFiles,
            uploadedBytes: po.UploadedBytes,
            uploadError: po.UploadError,
            ledgeredAt: po.LedgeredAt,
            startedAt: po.StartedAt,
            completedAt: po.CompletedAt,
            errorMessage: po.ErrorMessage,
            createdAt: po.CreatedAt);
    }

    public static CollectTaskPo ToPo(CollectTask entity)
    {
        return new CollectTaskPo
        {
            Id = entity.Id,
            TaskNo = entity.TaskNo,
            DeviceKey = entity.Device.Key,
            RecorderId = entity.RecorderId,
            RecorderCode = entity.RecorderCode,
            RecorderName = entity.Device.Name,
            RecorderSerial = entity.Device.Serial,
            Protocol = entity.Device.Protocol,
            SourceRoot = entity.Device.Root,
            PhysicalPortId = entity.Device.PhysicalPortId,
            OperatorUserId = entity.OperatorUserId,
            DeptId = entity.DeptId,
            Status = entity.Status,
            IsAuto = entity.IsAuto,
            IsEmergency = entity.IsEmergency,
            TotalFiles = entity.TotalFiles,
            CollectedFiles = entity.CollectedFiles,
            SkippedFiles = entity.SkippedFiles,
            FailedFiles = entity.FailedFiles,
            TotalBytes = entity.TotalBytes,
            CollectedBytes = entity.CollectedBytes,
            SpeedBytesPerSecond = entity.SpeedBytesPerSecond,
            SyncStatus = entity.SyncStatus,
            UploadedFiles = entity.UploadedFiles,
            UploadedBytes = entity.UploadedBytes,
            UploadError = entity.UploadError,
            LedgeredAt = entity.LedgeredAt,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            ErrorMessage = entity.ErrorMessage,
            CreatedAt = entity.CreatedAt
        };
    }

    // ========== 内部：从 Po 拼 DeviceRef（含历史数据兜底） ==========

    private static DeviceRef BuildDeviceRef(CollectTaskPo po)
    {
        return DeviceRef.Create(
            key: po.DeviceKey!,
            physicalPortId: po.PhysicalPortId,
            serial: po.RecorderSerial,
            root: string.IsNullOrWhiteSpace(po.SourceRoot) ? "unknown" : po.SourceRoot!,
            name: string.IsNullOrWhiteSpace(po.RecorderName) ? "unknown" : po.RecorderName,
            protocol: po.Protocol);
    }
}
