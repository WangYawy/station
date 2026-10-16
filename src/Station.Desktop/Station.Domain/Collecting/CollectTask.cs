using Station.Contracts;
using Station.Domain.Abstractions;
using Station.Domain.Collecting.Events;

namespace Station.Domain.Collecting;

/// <summary>
/// 采集任务聚合根。
/// 状态机：Created → Scanning → Collecting → Completed / Failed / Canceled / Interrupted
///                                ↕ Paused
/// </summary>
public sealed class CollectTask : AggregateRoot
{
    public long Id { get; private set; }
    public string TaskNo { get; private set; } = string.Empty;
    public string RecorderName { get; private set; } = string.Empty;
    public string? RecorderSerial { get; private set; }
    public ProtocolType Protocol { get; private set; }
    public string? SourceRoot { get; private set; }
    public long? OperatorUserId { get; private set; }
    public long? DeptId { get; private set; }
    public CollectTaskStatus Status { get; private set; }
    public bool IsAuto { get; private set; }
    public bool IsEmergency { get; private set; }
    public int TotalFiles { get; private set; }
    public int CollectedFiles { get; private set; }
    public int SkippedFiles { get; private set; }
    public int FailedFiles { get; private set; }
    public long TotalBytes { get; private set; }
    public long CollectedBytes { get; private set; }
    public double SpeedBytesPerSecond { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public UploadStatus SyncStatus { get; private set; }
    public int UploadedFiles { get; private set; }
    public long UploadedBytes { get; private set; }
    public string? UploadError { get; private set; }
    public DateTime? LedgeredAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CollectTask() { }

    public static CollectTask Create(
        long id,
        string taskNo,
        string recorderName,
        string? recorderSerial,
        ProtocolType protocol,
        string? sourceRoot,
        long? operatorUserId,
        long? deptId,
        bool isAuto,
        DateTime now)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(taskNo))
            throw new ArgumentException("任务编号不能为空。", nameof(taskNo));
        if (string.IsNullOrWhiteSpace(recorderName))
            throw new ArgumentException("记录仪名称不能为空。", nameof(recorderName));

        var task = new CollectTask
        {
            Id = id,
            TaskNo = taskNo,
            RecorderName = recorderName,
            RecorderSerial = recorderSerial,
            Protocol = protocol,
            SourceRoot = sourceRoot,
            OperatorUserId = operatorUserId,
            DeptId = deptId,
            Status = CollectTaskStatus.Created,
            IsAuto = isAuto,
            IsEmergency = false,
            SyncStatus = UploadStatus.Pending,
            CreatedAt = now
        };

        task.Raise(new CollectTaskCreated(
            Guid.NewGuid(), now, id, nameof(CollectTask),
            taskNo, recorderSerial ?? string.Empty, recorderName,
            operatorUserId, deptId, isAuto, false));

        return task;
    }

    public static CollectTask Restore(
        long id, string taskNo,
        string recorderName, string? recorderSerial, ProtocolType protocol,
        string? sourceRoot, long? operatorUserId, long? deptId,
        CollectTaskStatus status, bool isAuto, bool isEmergency,
        int totalFiles, int collectedFiles, int skippedFiles, int failedFiles,
        long totalBytes, long collectedBytes, double speedBytesPerSecond,
        DateTime? startedAt, DateTime? completedAt, string? errorMessage,
        UploadStatus syncStatus, int uploadedFiles, long uploadedBytes,
        string? uploadError, DateTime? ledgeredAt, DateTime createdAt)
        => new()
        {
            Id = id,
            TaskNo = taskNo,
            RecorderName = recorderName,
            RecorderSerial = recorderSerial,
            Protocol = protocol,
            SourceRoot = sourceRoot,
            OperatorUserId = operatorUserId,
            DeptId = deptId,
            Status = status,
            IsAuto = isAuto,
            IsEmergency = isEmergency,
            TotalFiles = totalFiles,
            CollectedFiles = collectedFiles,
            SkippedFiles = skippedFiles,
            FailedFiles = failedFiles,
            TotalBytes = totalBytes,
            CollectedBytes = collectedBytes,
            SpeedBytesPerSecond = speedBytesPerSecond,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ErrorMessage = errorMessage,
            SyncStatus = syncStatus,
            UploadedFiles = uploadedFiles,
            UploadedBytes = uploadedBytes,
            UploadError = uploadError,
            LedgeredAt = ledgeredAt,
            CreatedAt = createdAt
        };

    /// <summary>扫描完成，进入采集阶段。设置扫描统计。</summary>
    public void BeginCollecting(
        int totalFiles, int skippedFiles, long totalBytes, DateTime now)
    {
        EnsureStatus(CollectTaskStatus.Created, CollectTaskStatus.Scanning);

        TotalFiles = totalFiles;
        SkippedFiles = skippedFiles;
        TotalBytes = totalBytes;
        StartedAt ??= now;

        TransitionTo(CollectTaskStatus.Collecting, null, now);
    }

    /// <summary>暂停（仅 Collecting → Paused）。</summary>
    public void Pause(DateTime now)
    {
        if (Status != CollectTaskStatus.Collecting && Status != CollectTaskStatus.Scanning)
            throw new InvalidOperationException($"状态 {Status} 无法暂停。");

        TransitionTo(CollectTaskStatus.Paused, null, now);
    }

    /// <summary>恢复（仅 Paused → Collecting）。</summary>
    public void Resume(DateTime now)
    {
        if (Status != CollectTaskStatus.Paused)
            throw new InvalidOperationException($"状态 {Status} 无法恢复。");

        TransitionTo(CollectTaskStatus.Collecting, null, now);
    }

    /// <summary>
    /// 采集完成后更新聚合统计（由 ProgressAggregator 或单文件完成路径调用）。
    /// 幂等：只累加，由调用方保证每个 file 只调用一次。
    /// </summary>
    public void AddCollectedFile(long fileSize, DateTime now)
    {
        CollectedFiles++;
        CollectedBytes += fileSize;
        TouchedForProgress(now);
    }

    /// <summary>标记文件失败。不改变状态机。</summary>
    public void AddFailedFile(DateTime now)
    {
        FailedFiles++;
        TouchedForProgress(now);
    }

    /// <summary>更新上传进度聚合。</summary>
    public void AddUploadedFile(long fileSize, DateTime now)
    {
        UploadedFiles++;
        UploadedBytes += fileSize;
        TouchedForProgress(now);
    }

    /// <summary>汇总刷新（由后台合并器批量应用时调用）。</summary>
    public void ApplyAggregatedProgress(
        int collectedFiles, long collectedBytes, int failedFiles,
        double speedBytesPerSecond, DateTime now)
    {
        CollectedFiles = collectedFiles;
        CollectedBytes = collectedBytes;
        FailedFiles = failedFiles;
        SpeedBytesPerSecond = speedBytesPerSecond;
        TouchedForProgress(now);
    }

    /// <summary>任务正常完成。</summary>
    public void Complete(DateTime now)
    {
        if (Status is CollectTaskStatus.Completed or CollectTaskStatus.Canceled or CollectTaskStatus.Interrupted)
            return;

        TransitionTo(CollectTaskStatus.Completed, null, now);
        CompletedAt = now;

        Raise(new CollectTaskCompleted(
            Guid.NewGuid(), now, Id, nameof(CollectTask),
            TotalFiles, CollectedFiles, FailedFiles, CollectedBytes, now));
    }

    /// <summary>任务失败。</summary>
    public void Fail(string errorMessage, DateTime now)
    {
        if (Status is CollectTaskStatus.Completed or CollectTaskStatus.Failed)
            return;

        ErrorMessage = errorMessage;
        TransitionTo(CollectTaskStatus.Failed, errorMessage, now);
        CompletedAt = now;

        Raise(new CollectTaskFailed(
            Guid.NewGuid(), now, Id, nameof(CollectTask),
            errorMessage, now));
    }

    /// <summary>取消。</summary>
    public void Cancel(string? reason, DateTime now)
    {
        if (Status is CollectTaskStatus.Completed or CollectTaskStatus.Canceled)
            return;

        ErrorMessage = reason;
        TransitionTo(CollectTaskStatus.Canceled, reason, now);
        CompletedAt = now;
    }

    /// <summary>中断（设备断开等外部原因）。</summary>
    public void Interrupt(string reason, DateTime now)
    {
        if (Status is CollectTaskStatus.Completed or CollectTaskStatus.Interrupted)
            return;

        ErrorMessage = reason;
        TransitionTo(CollectTaskStatus.Interrupted, reason, now);
        CompletedAt = now;
    }

    /// <summary>标记为紧急优先。</summary>
    public void MarkEmergency(bool isEmergency, DateTime now)
    {
        if (IsEmergency == isEmergency) return;
        IsEmergency = isEmergency;

        Raise(new CollectTaskStatusChanged(
            Guid.NewGuid(), now, Id, nameof(CollectTask),
            Status, Status, null, isEmergency));
    }

    /// <summary>上传完成（Task 全部文件已上传）。</summary>
    public void MarkUploaded(DateTime now)
    {
        SyncStatus = UploadStatus.Uploaded;
        UploadError = null;
        TouchedForProgress(now);
    }

    /// <summary>上传部分失败。</summary>
    public void MarkUploadFailed(string? error, DateTime now)
    {
        SyncStatus = UploadStatus.Failed;
        UploadError = error;
        TouchedForProgress(now);
    }

    /// <summary>进入扫描阶段（仅 Created → Scanning）。</summary>
    public void BeginScanning(DateTime now)
    {
        EnsureStatus(CollectTaskStatus.Created);
        StartedAt ??= now;
        ErrorMessage = null;
        TransitionTo(CollectTaskStatus.Scanning, null, now);
    }

    /// <summary>追加错误信息（不触发状态变化）。</summary>
    public void AppendErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ErrorMessage = ErrorMessage is null
            ? message
            : ErrorMessage + "；" + message;
    }

    /// <summary>更新采集速度（聚合内部字段，不触发事件）。</summary>
    public void SetSpeed(double bytesPerSecond) => SpeedBytesPerSecond = bytesPerSecond;

    /// <summary>
    /// 状态转换
    /// </summary>
    private void TransitionTo(CollectTaskStatus target, string? reason, DateTime now)
    {
        var from = Status;
        if (from == target) return;

        Status = target;

        Raise(new CollectTaskStatusChanged(
            Guid.NewGuid(), now, Id, nameof(CollectTask),
            from, target, reason, IsEmergency));
    }

    private void TouchedForProgress(DateTime now)
    {
        // 进度变化不单独发事件；由 ProgressAggregator 合并后发 CollectTaskProgressChanged
        // 这里只做内部状态更新，避免每个文件完成都产生事件
        _ = now;
    }

    private void EnsureStatus(params CollectTaskStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvalidOperationException(
                $"状态 {Status} 不在允许范围 [{string.Join(",", allowed)}] 内。");
    }
}
