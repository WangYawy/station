using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Domain.Abstractions;

namespace Station.Infrastructure.Events;

/// <summary>
/// 后台 Dispatcher：从 Channel 读取事件，批量派发给 handler。
/// 提供两种触发：批量满或时间到。
/// </summary>
public sealed class ChannelEventDispatcher : BackgroundService
{
    private readonly ChannelDomainEventBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DomainEventBusOptions _options;
    private readonly ILogger<ChannelEventDispatcher> _logger;

    public ChannelEventDispatcher(
        IDomainEventBus bus,
        IServiceScopeFactory scopeFactory,
        IOptions<DomainEventBusOptions> options,
        ILogger<ChannelEventDispatcher> logger)
    {
        _bus = (ChannelDomainEventBus)bus;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _bus.Reader;
        var batch = new List<IDomainEvent>(_options.BatchSize);
        var flushInterval = TimeSpan.FromMilliseconds(_options.FlushIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                // 先取第一个（阻塞等待）
                var first = await reader.ReadAsync(stoppingToken);
                batch.Add(first);

                // 再尽力补满或到时间
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(flushInterval);

                try
                {
                    while (batch.Count < _options.BatchSize &&
                           reader.TryRead(out var next))
                    {
                        batch.Add(next);
                    }

                    // 未满时等待
                    while (batch.Count < _options.BatchSize)
                    {
                        var more = await reader.ReadAsync(timeoutCts.Token);
                        batch.Add(more);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 时间到，正常退出补满循环
                }

                await DispatchBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事件批量派发异常");
            }
        }
    }

    private async Task DispatchBatchAsync(
        IReadOnlyList<IDomainEvent> batch, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        foreach (var e in batch)
        {
            try
            {
                var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(e.GetType());
                var handlers = scope.ServiceProvider.GetServices(handlerType);

                foreach (var handler in handlers)
                {
                    var method = handlerType.GetMethod(
                        nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
                    await (Task)method.Invoke(handler, [e, ct])!;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "事件 {EventType} #{EventId} 处理失败",
                    e.GetType().Name, e.EventId);
                // 阶段一：失败仅记录，不重试（后续 Outbox 阶段再重试）
            }
        }
    }
}
