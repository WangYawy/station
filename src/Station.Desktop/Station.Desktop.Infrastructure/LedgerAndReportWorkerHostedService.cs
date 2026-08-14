using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Station.Application.Collecting;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Repositories;

namespace Station.Desktop.Infrastructure;

/// <summary>
/// 台账与上报后台任务：扫描"采集完成且未入台账"的任务，
/// 生成文件台账（SM3/FileNo/归属）并挂接平台元数据上报（经 Outbox 断网补报）。
/// </summary>
public sealed class LedgerAndReportWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public LedgerAndReportWorkerHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
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
                        t.Status == CollectTaskStatus.Completed && t.LedgeredAt == null))
                    .OrderBy(t => t.Id)
                    .ToList();

                var ledger = scope.ServiceProvider.GetRequiredService<IFileLedgerService>();
                foreach (var candidate in candidates)
                {
                    await ledger.ProcessCompletedTaskAsync(candidate.Id);
                }
            }
            catch
            {
                // 单轮失败不中断
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
