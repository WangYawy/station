namespace Station.Domain.Abstractions;

/// <summary>领域事件标记。所有事件不可变。</summary>
public interface IDomainEvent
{
    /// <summary>事件唯一 ID，用于幂等去重。</summary>
    Guid EventId { get; }

    /// <summary>事件发生时间（UTC）。</summary>
    DateTime OccurredAt { get; }

    /// <summary>所属聚合根 ID。</summary>
    long AggregateId { get; }

    /// <summary>所属聚合根类型（如 "CollectTask"）。</summary>
    string AggregateType { get; }
}
