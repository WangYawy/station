using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

/// <summary>文件上传进度。已由聚合内节流（5% 或 500ms）。</summary>
public sealed record CollectFileUploadProgressed(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    long TaskId,
    double Progress,
    long UploadedBytes,
    long TotalBytes) : IDomainEvent;
