namespace Station.Domain.Abstractions;

/// <summary>领域事件处理器。实现注册到 DI，由事件总线解析。</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}
