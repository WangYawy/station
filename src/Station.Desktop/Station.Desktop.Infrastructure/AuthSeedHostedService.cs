using Microsoft.Extensions.Hosting;
using Station.Infrastructure.Persistence;

namespace Station.Desktop.Infrastructure;

/// <summary>应用启动时初始化认证表结构与种子数据（幂等）。</summary>
public sealed class AuthSeedHostedService : IHostedService
{
    private readonly IAuthSeeder _seeder;

    public AuthSeedHostedService(IAuthSeeder seeder)
    {
        _seeder = seeder;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _seeder.EnsureAsync();

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
