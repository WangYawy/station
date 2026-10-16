using Station.Application.Collecting.Dtos;
using Station.Application.Recorders;

namespace Station.Application.Collecting;

public interface ICollectTaskService
{
    Task<CollectTaskDto> CreateTaskAsync(BoundDevice device, bool isAuto, CancellationToken ct = default);
    Task<CollectTaskDto> StartAsync(long taskId, CancellationToken ct = default);
    Task PauseAsync(long taskId, CancellationToken ct = default);
    Task ResumeAsync(long taskId, CancellationToken ct = default);
    Task CancelAsync(long taskId, CancellationToken ct = default);
    Task InterruptAsync(long taskId, string reason, CancellationToken ct = default);
    Task<bool> SetEmergencyAsync(long taskId, bool isEmergency, CancellationToken ct = default);
    Task<CollectTaskDto?> GetTaskAsync(long taskId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectTaskDto>> GetTasksAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<CollectTaskDto>> GetActiveTasksAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId, CancellationToken ct = default);

    /// <summary>获取任务的 UI 版本号（用于快照与事件的版本仲裁）。</summary>
    long GetUiVersion(long taskId);
}
