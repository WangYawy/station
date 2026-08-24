using Microsoft.Extensions.Hosting;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Station.Desktop.Infrastructure;

/// <summary>应用启动时初始化业务表结构与认证种子数据（幂等）。</summary>
public sealed class StationDbInitializerHostedService : IHostedService
{
    private readonly IAuthSeeder _authSeeder;
    private readonly IDatabaseInitializer _initializer;
    private readonly ILogger<StationDbInitializerHostedService> _logger;

    public StationDbInitializerHostedService(
        IAuthSeeder authSeeder,
        IDatabaseInitializer initializer,
        ILogger<StationDbInitializerHostedService> logger)
    {
        _authSeeder = authSeeder;
        _initializer = initializer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _initializer.EnsureCreated(
            typeof(CollectTask), typeof(CollectFile), typeof(Recorder), typeof(Alert),
            typeof(SyncOutbox), typeof(VideoFile), typeof(LicenseInfo), typeof(ClockState));
        // 存量库补列：紧急优先标记（新库由 CodeFirst 自动创建）
        _initializer.EnsureColumn("station_collect_task", "IsEmergency", "int");
        // 存量库补列：设备根路径（多设备/混合协议按任务路由）
        _initializer.EnsureColumn("station_collect_task", "SourceRoot", "varchar(512)");
        await _authSeeder.EnsureAsync();
        _logger.LogInformation("数据库初始化完成（建表/补列/认证种子）");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
