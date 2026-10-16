using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Domain.Abstractions;
using Station.Domain.Collecting;
using Station.Domain.Collecting.Events;

namespace Station.Infrastructure.Events;

/// <summary>
/// 进度合并器：订阅 CollectFileCollected，内存累加，
/// 每 200ms 批量写入 station_collect_task 并发出 CollectTaskProgressChanged 事件。
///
/// 设计要点：
///   - 单例；同时注册为 IHostedService 和 IDomainEventHandler&lt;CollectFileCollected&gt;
///   - HandleAsync 只做内存累加（Interlocked），永不阻塞事件总线
///   - 后台循环定期 Drain + 原子 UPDATE + 发事件
///   - 主线程写状态字段时走 UpdateStateOnly，不碰进度字段，避免覆盖
/// </summary>
public sealed class ProgressAggregator
    : BackgroundService, IDomainEventHandler<CollectFileCollected>
{
    private readonly ConcurrentDictionary<long, Accumulator> _accumulators = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DomainEventBusOptions _options;
    private readonly ILogger<ProgressAggregator> _logger;

    public ProgressAggregator(
        IServiceScopeFactory scopeFactory,
        IOptions<DomainEventBusOptions> options,
        ILogger<ProgressAggregator> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    // 事件处理：只做内存累加，永不阻塞

    public Task HandleAsync(CollectFileCollected e, CancellationToken ct = default)
    {
        var acc = _accumulators.GetOrAdd(e.TaskId, _ => new Accumulator());
        acc.Add(e.Size, failed: false);
        return Task.CompletedTask;
    }

    // 后台循环

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = Math.Max(50, _options.ProgressFlushIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProgressAggregator flush 异常");
            }
        }

        // 优雅退出：最后再冲一次
        try { await FlushAsync(CancellationToken.None); }
        catch { /* 忽略 */ }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        if (_accumulators.IsEmpty) return;

        // 1. Drain 所有累加器
        var snapshot = new List<FlushItem>(_accumulators.Count);
        foreach (var kv in _accumulators)
        {
            if (!_accumulators.TryRemove(kv.Key, out var acc)) continue;
            var drained = acc.Drain();
            if (drained.Files == 0 && drained.Failed == 0) continue;

            snapshot.Add(new FlushItem(
                kv.Key, drained.Files, drained.Bytes, drained.Failed, drained.Speed));
        }

        if (snapshot.Count == 0) return;

        // 2. 逐个原子 UPDATE（长连接，快）
        using var scope = _scopeFactory.CreateScope();
        var taskSync = scope.ServiceProvider.GetRequiredService<ICollectTaskSyncRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IDomainEventBus>();

        var progressEvents = new List<IDomainEvent>(snapshot.Count);

        foreach (var item in snapshot)
        {
            try
            {
                var snap = taskSync.IncrementProgress(
                    item.TaskId, item.Files, item.Bytes, item.Failed, item.Speed);

                if (snap is null)
                {
                    // 任务已不存在或已删除，跳过
                    continue;
                }

                progressEvents.Add(new CollectTaskProgressChanged(
                    Guid.NewGuid(), DateTime.UtcNow,
                    snap.TaskId, nameof(CollectTask),
                    snap.TotalFiles, snap.CollectedFiles, snap.FailedFiles,
                    snap.TotalBytes, snap.CollectedBytes, snap.SpeedBytesPerSecond));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ProgressAggregator 更新任务 {TaskId} 失败（files={Files}, bytes={Bytes}）",
                    item.TaskId, item.Files, item.Bytes);
                // 失败不再重投；下一个周期若有新事件会重新累加
            }
        }

        // 3. 批量发事件
        if (progressEvents.Count > 0)
        {
            try
            {
                await bus.PublishRangeAsync(progressEvents, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProgressAggregator 发布进度事件失败");
            }
        }
    }

    // 累加器（无锁）
    private sealed class Accumulator
    {
        private long _files;
        private long _bytes;
        private int _failed;
        private long _lastBytes;
        private DateTime _lastAt = DateTime.MinValue;

        public void Add(long size, bool failed)
        {
            if (failed) Interlocked.Increment(ref _failed);
            else
            {
                Interlocked.Increment(ref _files);
                Interlocked.Add(ref _bytes, size);
            }
        }

        public DrainResult Drain()
        {
            var files = (int)Interlocked.Exchange(ref _files, 0);
            var bytes = Interlocked.Exchange(ref _bytes, 0);
            var failed = Interlocked.Exchange(ref _failed, 0);

            var now = DateTime.UtcNow;
            double speed = 0;
            if (_lastAt != DateTime.MinValue && bytes > 0)
            {
                var dt = (now - _lastAt).TotalSeconds;
                if (dt > 0.001)
                    speed = bytes / dt;
            }
            _lastBytes += bytes;
            _lastAt = now;

            return new DrainResult(files, bytes, failed, speed);
        }
    }

    private readonly record struct DrainResult(int Files, long Bytes, int Failed, double Speed);
    private readonly record struct FlushItem(long TaskId, int Files, long Bytes, int Failed, double Speed);
}
