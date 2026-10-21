using Microsoft.Extensions.Logging;
using Station.Domain.Collecting;

namespace Station.Application.Collecting;

/// <summary>
/// 启动修复服务。
///
/// 【职责】
///   程序启动时扫描 DB，把异常状态下（Scanning / Collecting）的"僵尸"任务
///   转成 Interrupted，避免：
///     - UI 上"采集中"但实际停止不动
///     - 抢占活跃任务名额（SetEmergency 限制）
///     - 阻碍 FindResumeableTaskAsync 正确判定
///
/// 【调用时机】
///   App.OnShellOpened → RunStartupFlowAsync → 自检后
///
/// 【幂等性】
///   多次启动只有第一次起作用，后续查询已无异常任务。
/// </summary>
public interface IStartupRepairService
{
    /// <summary>执行修复，返回修复的任务数。</summary>
    Task<int> RepairAsync(CancellationToken ct = default);
}

public sealed class StartupRepairService : IStartupRepairService
{
    private readonly ICollectTaskService _collectService;
    private readonly ILogger<StartupRepairService> _logger;

    public StartupRepairService(
        ICollectTaskService collectService,
        ILogger<StartupRepairService> logger)
    {
        _collectService = collectService;
        _logger = logger;
    }

    public async Task<int> RepairAsync(CancellationToken ct = default)
    {
        // 1. 拉活跃任务
        IReadOnlyList<Dtos.CollectTaskDto> active;
        try
        {
            active = await _collectService.GetActiveTasksAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动修复：拉活跃任务失败");
            return 0;
        }

        if (active.Count == 0) return 0;

        // 2. 筛选异常状态
        var toRepair = active.Where(t =>
            t.Status is CollectTaskStatus.Scanning
                    or CollectTaskStatus.Collecting).ToList();

        if (toRepair.Count == 0) return 0;

        _logger.LogInformation("启动修复：{Count} 个异常任务待处理", toRepair.Count);

        // 3. 逐个中断
        var repaired = 0;
        foreach (var task in toRepair)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await _collectService.InterruptAsync(task.TaskId, "程序异常退出，任务中断");
                repaired++;
                _logger.LogInformation(
                    "启动修复：任务 {TaskNo}（{Status}）→ Interrupted",
                    task.TaskNo, task.Status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "启动修复：中断任务 {TaskNo} 失败", task.TaskNo);
            }
        }

        _logger.LogInformation("启动修复完成：修复 {Repaired}/{Total} 个任务",
            repaired, toRepair.Count);
        return repaired;
    }
}
