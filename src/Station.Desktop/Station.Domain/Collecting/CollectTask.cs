using System.Data;
using Station.Contracts;
using Station.Domain.Abstractions;

namespace Station.Domain.Collecting;

/// <summary>
/// 采集任务聚合根。
/// </summary>
public sealed class CollectTask : AggregateRoot
{
    public long Id { get; private set; }
    public string TaskNo { get; private set; } = string.Empty;

    /// <summary>记录仪台账 ID（0 表示无台账引用，兜底）。</summary>
    public long RecorderId { get; private set; }

    /// <summary>记录仪编码（可能为空串）。</summary>
    public string RecorderCode { get; private set; } = string.Empty;

    /// <summary>设备引用快照（不可变）。</summary>
    public DeviceRef Device { get; private set; } = null!;

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

    /// <summary>上传同步状态：Pending / Uploaded / Failed。</summary>
    public UploadStatus SyncStatus { get; private set; }

    /// <summary>已上传文件数。</summary>
    public int UploadedFiles { get; private set; }

    /// <summary>已上传字节数。</summary>
    public long UploadedBytes { get; private set; }

    /// <summary>上传失败原因。</summary>
    public string? UploadError { get; private set; }

    /// <summary>台账记账完成时间（MarkUploaded 时设置）。</summary>
    public DateTime? LedgeredAt { get; private set; }


    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CollectTask() { }

    // ========== 工厂 ==========

    /// <summary>创建采集任务。</summary>
    public static CollectTask Create(
        long id,
        string taskNo,
        long recorderId,
        string recorderCode,
        DeviceRef device,
        long? userId,
        long? deptId,
        bool isAuto,
        DateTime now)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(taskNo))
            throw new ArgumentException("任务号不能为空", nameof(taskNo));
        ArgumentNullException.ThrowIfNull(device);

        return new CollectTask
        {
            Id = id,
            TaskNo = taskNo,
            RecorderId = recorderId,
            RecorderCode = recorderCode ?? string.Empty,
            Device = device,
            OperatorUserId = userId,
            DeptId = deptId,
            IsAuto = isAuto,
            IsEmergency = false,
            Status = CollectTaskStatus.Created,
            SyncStatus = UploadStatus.Pending,
            UploadedFiles = 0,
            UploadedBytes = 0,
            CreatedAt = now
        };
    }

    /// <summary>从持久化恢复。</summary>
    public static CollectTask Restore(
        long id,
        string taskNo,
        long recorderId,
        string recorderCode,
        DeviceRef device,
        long? operatorUserId,
        long? deptId,
        CollectTaskStatus status,
        bool isAuto,
        bool isEmergency,
        int totalFiles,
        int collectedFiles,
        int skippedFiles,
        int failedFiles,
        long totalBytes,
        long collectedBytes,
        double speedBytesPerSecond,
        UploadStatus syncStatus,
        int uploadedFiles,
        long uploadedBytes,
        string? uploadError,
        DateTime? ledgeredAt,
        DateTime? startedAt,
        DateTime? completedAt,
        string? errorMessage,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new CollectTask
        {
            Id = id,
            TaskNo = taskNo,
            RecorderId = recorderId,
            RecorderCode = recorderCode,
            Device = device,
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
            SyncStatus = syncStatus,
            UploadedFiles = uploadedFiles,
            UploadedBytes = uploadedBytes,
            UploadError = uploadError,
            LedgeredAt = ledgeredAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ErrorMessage = errorMessage,
            CreatedAt = createdAt
        };
    }

    // ========== 采集状态机 ==========

    /// <summary>进入扫描阶段（Created / Interrupted / Failed / Paused → Scanning）。</summary>
    public void BeginScanning(DateTime now)
    {
        if (Status is not (CollectTaskStatus.Created
                            or CollectTaskStatus.Interrupted
                            or CollectTaskStatus.Failed
                            or CollectTaskStatus.Paused))
        {
            throw new InvalidOperationException($"任务状态 {Status} 不能进入扫描阶段");
        }
        Status = CollectTaskStatus.Scanning;
        StartedAt ??= now;
    }

    /// <summary>新建任务开始采集：设置统计字段。</summary>
    public void BeginCollecting(int totalFiles, int skippedFiles, long totalBytes, DateTime now)
    {
        if (Status != CollectTaskStatus.Scanning)
            throw new InvalidOperationException($"任务状态 {Status} 不能进入采集阶段");

        Status = CollectTaskStatus.Collecting;
        TotalFiles = totalFiles;
        SkippedFiles = skippedFiles;
        TotalBytes = totalBytes;
        StartedAt ??= now;
    }

    /// <summary>
    /// 续采：累加统计（不清空历史 Completed 计数）。
    /// 用于 Interrupted / Failed / Paused 任务重启后重试。
    /// </summary>
    public void ResumeCollecting(int newPendingFiles, long newPendingBytes, DateTime now)
    {
        if (Status is not (CollectTaskStatus.Interrupted
                            or CollectTaskStatus.Failed
                            or CollectTaskStatus.Paused))
        {
            throw new InvalidOperationException($"任务状态 {Status} 不能续采");
        }

        Status = CollectTaskStatus.Collecting;
        TotalFiles += newPendingFiles;
        TotalBytes += newPendingBytes;
    }

    /// <summary>采集 → 暂停。</summary>
    public void Pause(DateTime now)
    {
        if (Status != CollectTaskStatus.Collecting)
            throw new InvalidOperationException($"任务状态 {Status} 不能暂停");
        Status = CollectTaskStatus.Paused;
    }

    /// <summary>暂停 → 采集。</summary>
    public void Resume(DateTime now)
    {
        if (Status != CollectTaskStatus.Paused)
            throw new InvalidOperationException($"任务状态 {Status} 不能恢复");
        Status = CollectTaskStatus.Collecting;
    }

    /// <summary>标记任务完成。</summary>
    public void Complete(DateTime now)
    {
        Status = CollectTaskStatus.Completed;
        CompletedAt = now;
    }

    /// <summary>标记任务取消。</summary>
    public void Cancel(string? reason, DateTime now)
    {
        Status = CollectTaskStatus.Canceled;
        CompletedAt = now;
        if (!string.IsNullOrEmpty(reason)) AppendErrorMessage(reason);
    }

    /// <summary>标记任务中断（设备断开 / 程序崩溃）。</summary>
    public void Interrupt(string reason, DateTime now)
    {
        Status = CollectTaskStatus.Interrupted;
        CompletedAt = now;
        AppendErrorMessage(reason);
    }

    /// <summary>标记任务失败。</summary>
    public void Fail(string reason, DateTime now)
    {
        Status = CollectTaskStatus.Failed;
        CompletedAt = now;
        AppendErrorMessage(reason);
    }

    // =========== 计数 / 速度 / 错误 ==========

    /// <summary>累加已采集文件数 / 字节。</summary>
    public void AddCollectedFile(long fileSize, DateTime now)
    {
        CollectedFiles++;
        CollectedBytes += fileSize;
    }

    /// <summary>累加失败文件数。</summary>
    public void AddFailedFile(DateTime now)
    {
        FailedFiles++;
    }

    /// <summary>设置采集速度（字节/秒）。</summary>
    public void SetSpeed(double bytesPerSecond) => SpeedBytesPerSecond = bytesPerSecond;

    /// <summary>追加错误信息（多段用 " | " 拼接）。</summary>
    public void AppendErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ErrorMessage = string.IsNullOrEmpty(ErrorMessage)
            ? message
            : $"{ErrorMessage} | {message}";
    }

    /// <summary>设置紧急优先标记。</summary>
    public void MarkEmergency(bool isEmergency, DateTime now) => IsEmergency = isEmergency;

    // ========== 上传 / 台账（UploadService 使用） ==========

    /// <summary>
    /// 累加一个已上传成功文件的计数。
    /// 由 UploadService 每成功一个文件调用一次。
    /// </summary>
    public void AddUploadedFile(long fileSize, DateTime now)
    {
        UploadedFiles++;
        UploadedBytes += fileSize;
    }

    /// <summary>
    /// 标记任务上传完成（全部文件成功 + 台账写入完成）。
    /// - SyncStatus = "Uploaded"
    /// - LedgeredAt = now
    /// - UploadError 清空
    /// </summary>
    public void MarkUploaded(DateTime now)
    {
        SyncStatus = UploadStatus.Uploaded;
        LedgeredAt = now;
        UploadError = null;
    }

    /// <summary>
    /// 标记任务上传失败。
    /// - SyncStatus = "Failed"
    /// - UploadError 追加 / 设置失败原因
    /// - LedgeredAt 不清空（历史记账状态保留）
    /// </summary>
    public void MarkUploadFailed(string reason, DateTime now)
    {
        SyncStatus = UploadStatus.Failed;
        UploadError = reason;
    }
}
