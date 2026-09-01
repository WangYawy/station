using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Station.Application.Uploading;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Station.Desktop.Infrastructure;

/// <summary>
/// 上传后台任务：周期扫描"采集完成且未上传完"的任务并执行上传；
/// 失败文件保持 Pending（重试次数内）由下一轮扫描重试，等效"网络恢复事件触发重试"。
/// </summary>
public sealed class UploadWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<long, byte> _processing = new();
    private readonly ILogger<UploadWorkerHostedService> _logger;

    public UploadWorkerHostedService(IServiceScopeFactory scopeFactory, ILogger<UploadWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tasks = scope.ServiceProvider.GetRequiredService<IRepository<CollectTask>>();
                var candidates = (await tasks.GetListAsync(t =>
                        t.Status == CollectTaskStatus.Completed && t.SyncStatus != UploadStatus.Uploaded))
                    .OrderBy(t => t.Id)
                    .ToList();

                foreach (var candidate in candidates)
                {
                    if (!_processing.TryAdd(candidate.Id, 0))
                    {
                        continue;
                    }

                    try
                    {
                        var upload = scope.ServiceProvider.GetRequiredService<IUploadService>();
                        await upload.ProcessTaskAsync(candidate.Id);
                    }
                    finally
                    {
                        _processing.TryRemove(candidate.Id, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "上传轮询异常（单轮失败不中断）");
                // 单轮失败不中断后台服务
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
