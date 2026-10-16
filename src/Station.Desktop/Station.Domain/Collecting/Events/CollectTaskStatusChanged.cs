using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

/// <summary>任务状态变化。任何状态迁移都发这个事件。</summary>
public sealed record CollectTaskStatusChanged(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    CollectTaskStatus FromStatus,
    CollectTaskStatus ToStatus,
    string? Reason,
    bool IsEmergency) : IDomainEvent;
