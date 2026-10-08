using Station.Domain.Collecting;

namespace Station.Application.Collecting;

/// <summary>
/// 采集任务服务：接入→扫描筛选→复制校验→跳过已采集→中断/暂停/取消→完成后擦除。
/// </summary>
public interface ICollectTaskService
{
    Task<CollectTaskDto> CreateTaskAsync(CollectDeviceInfo device, bool isAuto, CancellationToken ct = default);

    Task<CollectTaskDto> StartAsync(long taskId, CancellationToken ct = default);

    Task PauseAsync(long taskId, CancellationToken ct = default);

    Task ResumeAsync(long taskId, CancellationToken ct = default);

    Task CancelAsync(long taskId, CancellationToken ct = default);

    /// <summary>标记/取消紧急优先；达到上限返回 false（不生效）。</summary>
    Task<bool> SetEmergencyAsync(long taskId, bool isEmergency, CancellationToken ct = default);

    /// <summary>设备拔出/断线：任务中断，未完成文件标异常/取消。</summary>
    Task InterruptAsync(long taskId, string reason, CancellationToken ct = default);

    Task<CollectTaskDto?> GetTaskAsync(long taskId, CancellationToken ct = default);

    Task<IReadOnlyList<CollectTaskDto>> GetTasksAsync(int count, CancellationToken ct = default);

    Task<IReadOnlyList<CollectTaskDto>> GetActiveTasksAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId, CancellationToken ct = default);
}
