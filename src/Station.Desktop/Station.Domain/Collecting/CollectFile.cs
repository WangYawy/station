using Station.Domain.Abstractions;
using Station.Domain.Collecting.Events;

namespace Station.Domain.Collecting;

/// <summary>
/// 采集文件聚合根。
/// 采集状态机：Pending → Skipped / Copying → Verifying → Completed
///                                │
///                                └─→ Failed / Canceled / Abnormal
/// 上传状态机：Pending → Uploading → Uploaded / Failed
/// </summary>
public sealed class CollectFile : AggregateRoot
{
    public long Id { get; private set; }
    public long TaskId { get; private set; }
    public string TaskNo { get; private set; } = string.Empty;
    public string FileNo { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string? Extension { get; private set; }
    public string? RelativePath { get; private set; }
    public long Size { get; private set; }
    public string? Fingerprint { get; private set; }
    public string? ContentDigest { get; private set; }
    public string? DigestAlgorithm { get; private set; }
    public string? Signature { get; private set; }
    public string? SignatureAlgorithm { get; private set; }
    public bool LocalEncrypted { get; private set; }
    public long EncryptedSize { get; private set; }
    public CollectFileStatus Status { get; private set; }
    public double Progress { get; private set; }
    public double SpeedBytesPerSecond { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? CollectedAt { get; private set; }
    public DateTime? OriginalModifiedAt { get; private set; }

    public UploadStatus SyncStatus { get; private set; }
    public double UploadProgress { get; private set; }
    public int UploadRetryCount { get; private set; }
    public string? RemotePath { get; private set; }
    public string? UploadError { get; private set; }

    // ---- 上传进度节流状态（聚合内部，不参与持久化） ----
    private long _lastProgressEventBytes;
    private DateTime _lastProgressEventAt = DateTime.MinValue;
    private const int ProgressEventPercentStep = 5;
    private static readonly TimeSpan ProgressEventMinInterval = TimeSpan.FromMilliseconds(500);

    private CollectFile() { }

    public static CollectFile Create(
        long id, long taskId, string taskNo, string fileNo,
        string fileName, string? extension, string? relativePath,
        long size, string? fingerprint, DateTime? originalModifiedAt,
        bool skip, DateTime now)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (taskId <= 0) throw new ArgumentOutOfRangeException(nameof(taskId));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("文件名不能为空。", nameof(fileName));

        return new CollectFile
        {
            Id = id,
            TaskId = taskId,
            TaskNo = taskNo,
            FileNo = fileNo,
            FileName = fileName,
            Extension = extension,
            RelativePath = relativePath,
            Size = size,
            Fingerprint = fingerprint,
            OriginalModifiedAt = originalModifiedAt,
            Status = skip ? CollectFileStatus.Skipped : CollectFileStatus.Pending,
            SyncStatus = UploadStatus.Pending
        };
    }

    public static CollectFile Restore(
        long id, long taskId, string taskNo, string fileNo,
        string fileName, string? extension, string? relativePath, long size,
        string? fingerprint, string? contentDigest, string? digestAlgorithm,
        string? signature, string? signatureAlgorithm,
        bool localEncrypted, long encryptedSize,
        CollectFileStatus status, double progress, double speedBytesPerSecond,
        string? errorMessage, DateTime? collectedAt, DateTime? originalModifiedAt,
        UploadStatus syncStatus, double uploadProgress, int uploadRetryCount,
        string? remotePath, string? uploadError)
        => new()
        {
            Id = id,
            TaskId = taskId,
            TaskNo = taskNo,
            FileNo = fileNo,
            FileName = fileName,
            Extension = extension,
            RelativePath = relativePath,
            Size = size,
            Fingerprint = fingerprint,
            ContentDigest = contentDigest,
            DigestAlgorithm = digestAlgorithm,
            Signature = signature,
            SignatureAlgorithm = signatureAlgorithm,
            LocalEncrypted = localEncrypted,
            EncryptedSize = encryptedSize,
            Status = status,
            Progress = progress,
            SpeedBytesPerSecond = speedBytesPerSecond,
            ErrorMessage = errorMessage,
            CollectedAt = collectedAt,
            OriginalModifiedAt = originalModifiedAt,
            SyncStatus = syncStatus,
            UploadProgress = uploadProgress,
            UploadRetryCount = uploadRetryCount,
            RemotePath = remotePath,
            UploadError = uploadError
        };

    // =========================================================
    // 采集状态机
    // =========================================================

    /// <summary>开始复制。</summary>
    public void BeginCopy(DateTime now)
    {
        EnsureStatus(CollectFileStatus.Pending, CollectFileStatus.Failed);
        Status = CollectFileStatus.Copying;
        Progress = 0;
        ErrorMessage = null;
        _ = now;
    }

    /// <summary>
    /// 报告采集进度。
    /// 内部节流：进度变化 ≥ 10% 才更新字段（DB 写入由调用方控制）。
    /// </summary>
    public bool ReportCopyProgress(double progress)
    {
        if (Status != CollectFileStatus.Copying) return false;
        if (progress < 0 || progress > 1)
            throw new ArgumentOutOfRangeException(nameof(progress));

        var old = Progress;
        Progress = progress;
        return progress - old >= 0.1 || progress >= 1.0;
    }

    /// <summary>进入校验阶段。</summary>
    public void BeginVerify()
    {
        EnsureStatus(CollectFileStatus.Copying);
        Status = CollectFileStatus.Verifying;
    }

    /// <summary>
    /// 采集完成：设置摘要、签名、加密信息。
    /// 触发 CollectFileCollected 事件。
    /// </summary>
    public void CompleteCollecting(
        string? contentDigest, string? digestAlgorithm,
        string? signature, string? signatureAlgorithm,
        bool localEncrypted, long encryptedSize,
        DateTime now)
    {
        EnsureStatus(CollectFileStatus.Copying, CollectFileStatus.Verifying);

        ContentDigest = contentDigest;
        DigestAlgorithm = digestAlgorithm;
        Signature = signature;
        SignatureAlgorithm = signatureAlgorithm;
        LocalEncrypted = localEncrypted;
        EncryptedSize = encryptedSize;

        Status = CollectFileStatus.Completed;
        Progress = 1;
        CollectedAt = now;
        ErrorMessage = null;

        Raise(new CollectFileCollected(
            Guid.NewGuid(), now, Id, nameof(CollectFile),
            TaskId, FileName, Size, contentDigest, digestAlgorithm));
    }

    /// <summary>采集失败。</summary>
    public void FailCollecting(string error, DateTime now)
    {
        Status = CollectFileStatus.Failed;
        ErrorMessage = error;

        Raise(new CollectFileAbnormal(
            Guid.NewGuid(), now, Id, nameof(CollectFile),
            TaskId, FileName, CollectFileStatus.Failed, error));
    }

    /// <summary>取消。</summary>
    public void Cancel(string reason, DateTime now)
    {
        if (Status == CollectFileStatus.Completed) return;
        Status = CollectFileStatus.Canceled;
        ErrorMessage = reason;

        Raise(new CollectFileAbnormal(
             Guid.NewGuid(), now, Id, nameof(CollectFile),
             TaskId, FileName, CollectFileStatus.Canceled, reason));
    }

    /// <summary>中断（设备断开）。</summary>
    public void Interrupt(string reason, DateTime now)
    {
        if (Status == CollectFileStatus.Completed) return;
        Status = CollectFileStatus.Abnormal;
        ErrorMessage = reason;

        Raise(new CollectFileAbnormal(
            Guid.NewGuid(), now, Id, nameof(CollectFile),
            TaskId, FileName, CollectFileStatus.Abnormal, reason));
    }

    // =========================================================
    // 上传状态机
    // =========================================================

    /// <summary>开始上传。</summary>
    public void BeginUpload(long uploadSize, DateTime now)
    {
        EnsureStatusForUpload(UploadStatus.Pending, UploadStatus.Failed);
        SyncStatus = UploadStatus.Uploading;
        UploadProgress = 0;
        UploadError = null;

        Raise(new CollectFileUploadStarted(
            Guid.NewGuid(), now, Id, nameof(CollectFile),
            TaskId, FileName, uploadSize));
    }

    /// <summary>
    /// 报告上传进度。
    /// 内部节流：5% 档位 且 距上次 ≥ 500ms 才发事件。
    /// 返回 true 表示触发了事件。
    /// </summary>
    public bool ReportUploadProgress(long uploadedBytes, DateTime now)
    {
        if (SyncStatus != UploadStatus.Uploading) return false;
        if (uploadedBytes < 0 || uploadedBytes > Size)
            throw new ArgumentOutOfRangeException(nameof(uploadedBytes));

        UploadProgress = Size > 0 ? (double)uploadedBytes / Size : 0;

        var percent = (int)(UploadProgress * 100);
        var lastPercent = Size > 0 ? (int)((double)_lastProgressEventBytes / Size * 100) : 0;
        var timeSince = now - _lastProgressEventAt;

        var crossedStep = percent / ProgressEventPercentStep > lastPercent / ProgressEventPercentStep;
        var timedOut = timeSince >= ProgressEventMinInterval;

        if (!crossedStep && !timedOut) return false;

        _lastProgressEventBytes = uploadedBytes;
        _lastProgressEventAt = now;

        Raise(new CollectFileUploadProgressed(
            Guid.NewGuid(), now, Id, nameof(CollectFile),
            TaskId, UploadProgress, uploadedBytes, Size));
        return true;
    }

    /// <summary>上传成功。</summary>
    public void CompleteUpload(string remotePath, DateTime now)
    {
        EnsureStatusForUpload(UploadStatus.Uploading);

        SyncStatus = UploadStatus.Uploaded;
        UploadProgress = 1;
        RemotePath = remotePath;
        UploadError = null;

        Raise(new CollectFileUploaded(
            Guid.NewGuid(), now, Id, nameof(CollectFile),
            TaskId, FileName, Size, remotePath));
    }

    /// <summary>上传失败。</summary>
    public void FailUpload(string error, int maxRetry, DateTime now)
    {
        UploadRetryCount++;
        UploadError = error;
        SyncStatus = UploadRetryCount >= maxRetry
            ? UploadStatus.Failed
            : UploadStatus.Pending;

        Raise(new CollectFileUploadFailed(
            Guid.NewGuid(), now, Id, nameof(CollectFile),
            TaskId, FileName, UploadRetryCount, error));
    }

    /// <summary>重置为待上传（重试入口）。</summary>
    public void ResetForRetry(DateTime now)
    {
        if (SyncStatus != UploadStatus.Failed) return;
        SyncStatus = UploadStatus.Pending;
        UploadError = null;
        _ = now;
    }

    public void ResetToPending()
    {
        if (Status is CollectFileStatus.Copying or CollectFileStatus.Verifying)
        {
            Status = CollectFileStatus.Pending;
            Progress = 0;
        }
    }

    public void SetSpeed(double bytesPerSecond) => SpeedBytesPerSecond = bytesPerSecond;

    private void EnsureStatus(params CollectFileStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvalidOperationException(
                $"文件状态 {Status} 不在允许范围 [{string.Join(",", allowed)}] 内。");
    }

    private void EnsureStatusForUpload(params UploadStatus[] allowed)
    {
        if (!allowed.Contains(SyncStatus))
            throw new InvalidOperationException(
                $"上传状态 {SyncStatus} 不在允许范围 [{string.Join(",", allowed)}] 内。");
    }
}
