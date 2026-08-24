using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Station.Application.Audit;
using Station.Domain.Entities;
using Station.Infrastructure.Backup;
using Microsoft.Extensions.Logging;

namespace Station.Desktop.Infrastructure;

/// <summary>本地数据库每日自动备份（默认 03:00，保留 7 份），备份动作留审计。</summary>
public sealed class LocalBackupWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackupOptions _options;
    private readonly ILogger<LocalBackupWorkerHostedService> _logger;

    public LocalBackupWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackupOptions> options,
        ILogger<LocalBackupWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRun() - DateTime.Now;
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

            if (_options.Enabled)
            {
                await RunBackupAsync(stoppingToken);
            }
        }
    }

    private DateTime NextRun()
    {
        var next = DateTime.Today.AddHours(_options.Hour).AddMinutes(_options.Minute);
        return next <= DateTime.Now ? next.AddDays(1) : next;
    }

    private async Task RunBackupAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        try
        {
            var path = await backups.CreateBackupAsync();
            var list = backups.ListBackups();
            _logger.LogInformation("本地数据库备份完成：{File}（保留 {Count} 份）", Path.GetFileName(path), list.Count);
            await audit.WriteAsync(new AuditLog
            {
                OperatorAccount = "system",
                OperationType = "backup.auto",
                Target = Path.GetFileName(path),
                Detail = $"本地库每日备份完成，当前保留 {list.Count} 份",
                Result = 1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "本地数据库备份失败");
            await audit.WriteAsync(new AuditLog
            {
                OperatorAccount = "system",
                OperationType = "backup.auto",
                Detail = $"本地库备份失败：{ex.Message}",
                Result = 0
            });
        }
    }
}
