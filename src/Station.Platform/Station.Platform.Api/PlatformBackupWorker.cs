using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Station.Application.Audit;
using Station.Domain.Entities;
using Station.Infrastructure.Backup;
using Microsoft.Extensions.Logging;

namespace Station.Platform.Api;

/// <summary>平台数据库每日自动备份（默认 03:00，保留 30 份），备份动作留审计。</summary>
public sealed class PlatformBackupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackupOptions _options;
    private readonly ILogger<PlatformBackupWorker> _logger;

    public PlatformBackupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BackupOptions> options,
        ILogger<PlatformBackupWorker> logger)
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
                await Task.Delay(delay, stoppingToken);
            }

            if (_options.Enabled)
            {
                await RunBackupAsync();
            }
        }
    }

    private DateTime NextRun()
    {
        var next = DateTime.Today.AddHours(_options.Hour).AddMinutes(_options.Minute);
        return next <= DateTime.Now ? next.AddDays(1) : next;
    }

    private async Task RunBackupAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        try
        {
            var path = await backups.CreateBackupAsync();
            var list = backups.ListBackups();
            _logger.LogInformation("平台数据库备份完成：{File}（保留 {Count} 份）", Path.GetFileName(path), list.Count);
            await audit.WriteAsync(new AuditLog
            {
                OperatorAccount = "system",
                OperationType = "backup.auto",
                Target = Path.GetFileName(path),
                Detail = $"平台库每日备份完成，当前保留 {list.Count} 份",
                Result = 1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "平台数据库备份失败");
            await audit.WriteAsync(new AuditLog
            {
                OperatorAccount = "system",
                OperationType = "backup.auto",
                Detail = $"平台库备份失败：{ex.Message}",
                Result = 0
            });
        }
    }
}
