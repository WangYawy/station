using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

/// <summary>文件未正常完成（失败/取消/中断）。</summary>
public sealed record CollectFileAbnormal(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    long TaskId,
    string FileName,
    CollectFileStatus Status,
    string Reason) : IDomainEvent;
