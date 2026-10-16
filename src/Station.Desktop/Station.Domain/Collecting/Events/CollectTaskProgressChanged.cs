using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

/// <summary>任务级进度变化。由 ProgressAggregator 合并后发出。</summary>
public sealed record CollectTaskProgressChanged(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    int TotalFiles,
    int CollectedFiles,
    int FailedFiles,
    long TotalBytes,
    long CollectedBytes,
    double SpeedBytesPerSecond) : IDomainEvent;
