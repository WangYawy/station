using Microsoft.Extensions.Hosting;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.Persistence;

namespace Station.Desktop.Infrastructure;

/// <summary>应用启动时初始化业务表结构与认证种子数据（幂等）。</summary>
public sealed class StationDbInitializerHostedService : IHostedService
{
    private readonly IAuthSeeder _authSeeder;
    private readonly IDatabaseInitializer _initializer;

    public StationDbInitializerHostedService(IAuthSeeder authSeeder, IDatabaseInitializer initializer)
    {
        _authSeeder = authSeeder;
        _initializer = initializer;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _initializer.EnsureCreated(
            typeof(CollectTask), typeof(CollectFile), typeof(Recorder), typeof(Alert),
            typeof(SyncOutbox), typeof(VideoFile));
        await _authSeeder.EnsureAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
