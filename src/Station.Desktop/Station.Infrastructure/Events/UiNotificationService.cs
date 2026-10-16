using Microsoft.Extensions.Logging;
using Station.Application.Notifications;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Events;

/// <summary>
/// UI 通知服务实现：进程内同步派发。
/// 每个订阅者独立 try/catch，避免一个 handler 异常影响其他。
/// </summary>
public sealed class UiNotificationService : IUiNotificationService
{
    private readonly ILogger<UiNotificationService> _logger;

    public UiNotificationService(ILogger<UiNotificationService> logger)
        => _logger = logger;

    // =========================================================
    // 设备
    // =========================================================

    public event EventHandler<DeviceConnectedEvent>? DeviceConnected;
    public event EventHandler<DeviceDisconnectedEvent>? DeviceDisconnected;
    public event EventHandler<DeviceBoundEvent>? DeviceBound;
    public event EventHandler<DeviceRejectedEvent>? DeviceRejected;

    public void PublishDeviceConnected(DeviceConnectedEvent e)
        => Invoke(DeviceConnected, e, "DeviceConnected");

    public void PublishDeviceDisconnected(DeviceDisconnectedEvent e)
        => Invoke(DeviceDisconnected, e, "DeviceDisconnected");

    public void PublishDeviceBound(DeviceBoundEvent e)
        => Invoke(DeviceBound, e, "DeviceBound");

    public void PublishDeviceRejected(DeviceRejectedEvent e)
        => Invoke(DeviceRejected, e, "DeviceRejected");

    // =========================================================
    // 任务
    // =========================================================

    public event EventHandler<TaskStatusChangedEvent>? TaskStatusChanged;
    public event EventHandler<TaskProgressUpdatedEvent>? TaskProgressUpdated;
    public event EventHandler<TaskFileCompletedEvent>? TaskFileCompleted;

    public void PublishTaskStatus(long taskId, CollectTaskStatus status, bool isEmergency)
        => Invoke(TaskStatusChanged,
                  new TaskStatusChangedEvent(taskId, status, isEmergency),
                  "TaskStatusChanged");

    public void PublishTaskProgress(long taskId, double progress, double speedBytesPerSecond)
        => Invoke(TaskProgressUpdated,
                  new TaskProgressUpdatedEvent(taskId, progress, speedBytesPerSecond),
                  "TaskProgressUpdated");

    public void PublishTaskFileCompleted(long taskId, long fileId, CollectFileStatus status)
        => Invoke(TaskFileCompleted,
                  new TaskFileCompletedEvent(taskId, fileId, status),
                  "TaskFileCompleted");

    // =========================================================
    // 内部：逐个订阅者独立 try/catch
    // =========================================================

    private void Invoke<T>(EventHandler<T>? handlers, T e, string eventName)
    {
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                handler(this, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UI 事件 {Event} 处理异常", eventName);
            }
        }
    }
}
