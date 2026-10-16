using Microsoft.Extensions.DependencyInjection;
using Station.Domain.Abstractions;

namespace Station.Infrastructure.Events;

/// <summary>
/// 同步事件总线：立即调用所有 handler。
/// 用于开发调试或低负载场景。生产环境推荐 Channel 模式。
/// </summary>
public sealed class SyncDomainEventBus : IDomainEventBus
{
    private readonly IServiceProvider _sp;

    public SyncDomainEventBus(IServiceProvider sp) => _sp = sp;

    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handlers = _sp.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, [domainEvent, ct])!;
        }
    }

    public async Task PublishRangeAsync(
        IReadOnlyList<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var e in events)
        {
            await PublishAsync(e, ct);
        }
    }
}
