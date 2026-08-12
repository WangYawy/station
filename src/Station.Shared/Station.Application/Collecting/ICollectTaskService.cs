namespace Station.Application.Collecting;

/// <summary>
/// 采集任务服务：接入→扫描筛选→复制校验→跳过已采集→中断/暂停/取消→完成后擦除。
/// </summary>
public interface ICollectTaskService
{
    Task<CollectTaskDto> CreateTaskAsync(CollectDeviceInfo device, bool isAuto);

    Task<CollectTaskDto> StartAsync(long taskId);

    Task PauseAsync(long taskId);

    Task ResumeAsync(long taskId);

    Task CancelAsync(long taskId);

    /// <summary>设备拔出/断线：任务中断，未完成文件标异常/取消。</summary>
    Task InterruptAsync(long taskId, string reason);

    Task<CollectTaskDto?> GetTaskAsync(long taskId);

    Task<IReadOnlyList<CollectTaskDto>> GetTasksAsync(int count);

    Task<IReadOnlyList<CollectTaskDto>> GetActiveTasksAsync();

    Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId);
}
