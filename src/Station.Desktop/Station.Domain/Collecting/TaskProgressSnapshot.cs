namespace Station.Domain.Collecting;

/// <summary>任务进度快照（用于 ProgressAggregator 发布事件）。</summary>
public sealed record TaskProgressSnapshot(
    long TaskId,
    int TotalFiles,
    int CollectedFiles,
    int FailedFiles,
    long TotalBytes,
    long CollectedBytes,
    double SpeedBytesPerSecond);
