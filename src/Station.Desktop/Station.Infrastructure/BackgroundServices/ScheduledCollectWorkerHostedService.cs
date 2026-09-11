using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Contracts;
using Microsoft.Extensions.Logging;
using Station.Domain.Collecting;

namespace Station.Desktop.Infrastructure;

/// <summary>后台定时采集：每日指定时间自动开始采集（需记录仪已连接/模拟源可用）。</summary>
public sealed class ScheduledCollectWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CollectOptions _options;
    private readonly ILogger<ScheduledCollectWorkerHostedService> _logger;

    public ScheduledCollectWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<CollectOptions> options,
        ILogger<ScheduledCollectWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var protocol = _options.SourceMode == "mtp" ? ProtocolType.Mtp : ProtocolType.Ums;
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = NextRun();
            var delay = next - DateTime.Now;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }

            if (!_options.ScheduledCollectEnabled)
            {
                continue;
            }

            using var scope = _scopeFactory.CreateScope();
            var collect = scope.ServiceProvider.GetRequiredService<ICollectTaskService>();
            try
            {
                await collect.CreateTaskAsync(
                    new CollectDeviceInfo("定时采集", "SCHEDULED", protocol),
                    isAuto: true);
            }
            catch
            {
                _logger.LogWarning("定时采集触发失败（无设备/未绑定等），下轮重试");
                // 无设备等场景：交由采集流程报警，定时任务下轮重试
            }
        }
    }

    private DateTime NextRun()
    {
        var next = DateTime.Today.AddHours(_options.ScheduleHour).AddMinutes(_options.ScheduleMinute);
        return next <= DateTime.Now ? next.AddDays(1) : next;
    }
}
