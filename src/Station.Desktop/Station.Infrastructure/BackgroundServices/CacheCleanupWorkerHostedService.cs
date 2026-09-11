using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Station.Application.Audit;
using Station.Application.Collecting;
using Station.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Station.Desktop.Infrastructure;

/// <summary>本地加密缓存每日清理（默认 04:00，保留天数可配），清理动作留审计。</summary>
public sealed class CacheCleanupWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CollectOptions _options;
    private readonly ILogger<CacheCleanupWorkerHostedService> _logger;

    public CacheCleanupWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<CollectOptions> options,
        ILogger<CacheCleanupWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

            using var scope = _scopeFactory.CreateScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<ICacheCleanupService>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
            var (count, bytes) = await cleanup.CleanupAsync();
            await audit.WriteAsync(new AuditLog
            {
                OperatorAccount = "system",
                OperationType = "cache.cleanup",
                Detail = $"清理超期缓存 {count} 个（{bytes / 1024 / 1024}MB，保留 {_options.CacheRetentionDays} 天）",
                Result = 1
            });
        }
    }

    private DateTime NextRun()
    {
        var next = DateTime.Today.AddHours(_options.CacheCleanupHour).AddMinutes(_options.CacheCleanupMinute);
        return next <= DateTime.Now ? next.AddDays(1) : next;
    }
}
