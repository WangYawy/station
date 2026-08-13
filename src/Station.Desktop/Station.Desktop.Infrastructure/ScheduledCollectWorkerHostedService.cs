using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Station.Application.Collecting;
using Station.Contracts;

namespace Station.Desktop.Infrastructure;

/// <summary>后台定时采集：每日指定时间自动开始采集（需记录仪已连接/模拟源可用）。</summary>
public sealed class ScheduledCollectWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CollectOptions _options;

    public ScheduledCollectWorkerHostedService(IServiceScopeFactory scopeFactory, IOptions<CollectOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = NextRun();
            var delay = next - DateTime.Now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
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
                    new CollectDeviceInfo("定时采集", "SCHEDULED", ProtocolType.Ums),
                    isAuto: true);
            }
            catch
            {
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
