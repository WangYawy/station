using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Domain.Abstractions;

namespace Station.Infrastructure.Events;

/// <summary>
/// Channel 模式事件总线：写入内存有界 Channel，由后台 Dispatcher 批量派发。
/// 生产者永不阻塞（除 BlockProducer 策略），派发失败不重试（依赖领域操作本身的 DB 事务）。
/// </summary>
public sealed class ChannelDomainEventBus : IDomainEventBus
{
    private readonly Channel<IDomainEvent> _channel;
    private readonly ILogger<ChannelDomainEventBus> _logger;

    public ChannelDomainEventBus(
        IOptions<DomainEventBusOptions> options,
        ILogger<ChannelDomainEventBus> logger)
    {
        _logger = logger;
        var opt = options.Value;

        _channel = Channel.CreateBounded<IDomainEvent>(new BoundedChannelOptions(opt.ChannelCapacity)
        {
            FullMode = opt.OverflowPolicy switch
            {
                ChannelOverflowPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
                ChannelOverflowPolicy.DropNewest => BoundedChannelFullMode.DropWrite,
                ChannelOverflowPolicy.BlockProducer => BoundedChannelFullMode.Wait,
                _ => BoundedChannelFullMode.DropOldest
            },
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_channel.Writer.TryWrite(domainEvent))
        {
            _logger.LogWarning(
                "事件总线 Channel 已满，丢弃事件 {EventType} #{EventId}",
                domainEvent.GetType().Name, domainEvent.EventId);
        }
        return Task.CompletedTask;
    }

    public async Task PublishRangeAsync(
        IReadOnlyList<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var e in events)
        {
            await PublishAsync(e, ct);
        }
    }

    internal ChannelReader<IDomainEvent> Reader => _channel.Reader;
}
