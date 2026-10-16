namespace Station.Domain.Abstractions;

/// <summary>
/// 领域事件总线抽象。
/// 实现可以是同步立即派发、内存 Channel 异步派发、或 Outbox 持久化派发。
/// </summary>
public interface IDomainEventBus
{
    /// <summary>发布单个事件。</summary>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default);

    /// <summary>批量发布事件（聚合根发布后调用）。</summary>
    Task PublishRangeAsync(
        IReadOnlyList<IDomainEvent> events, CancellationToken ct = default);
}
