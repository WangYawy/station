using System.Collections.Concurrent;
using Station.Domain.Enums;

namespace Station.Application.UsbPortCard.Events;
public sealed class UsbPortCardEventService : IUsbPortCardEventService, IDisposable
{
    // 标准事件委托
    public event EventHandler<TaskProgressUpdatedEvent>? TaskProgressUpdated;
    public event EventHandler<TaskStatusChangedEvent>? TaskStatusChanged;
    public event EventHandler<DeviceConnectedEvent>? DeviceConnected;
    public event EventHandler<DeviceDisconnectedEvent>? DeviceDisconnected;

    // 节流缓存：每个 TaskId 保留最新的进度值
    private readonly ConcurrentDictionary<long, (double Progress, double Speed)> _progressCache = new();
    private readonly System.Timers.Timer _flushTimer;
    private readonly object _lock = new();

    public UsbPortCardEventService()
    {
        // 每 500ms 刷新一次进度（合并高频更新）
        _flushTimer = new System.Timers.Timer(500);
        _flushTimer.Elapsed += (_, _) => FlushProgress();
        _flushTimer.AutoReset = true;
        _flushTimer.Start();
    }

    // 发布进度：只更新缓存，不立即触发事件
    public void PublishTaskProgress(long taskId, double progress, double speed)
    {
        _progressCache[taskId] = (progress, speed);
    }

    // 定时刷新：将缓存中的最新进度批量触发事件，在CollectTaskService中发布
    private void FlushProgress()
    {
        if (_progressCache.IsEmpty || TaskProgressUpdated is null) return;

        var snapshot = _progressCache.ToArray();
        _progressCache.Clear();

        foreach (var (taskId, (progress, speed)) in snapshot)
        {
            TaskProgressUpdated?.Invoke(this, new TaskProgressUpdatedEvent(taskId, progress, speed));
        }
    }

    // 状态变化：立即发布（无延迟） 在CollectTaskService中发布
    public void PublishTaskStatus(long taskId, CollectTaskStatus status, bool isEmergency)
    {
        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEvent(taskId, status, isEmergency));
    }
    // 设备连接：立即发布（无延迟）在RecorderConnectMonitor中发布
    public void PublishDeviceConnected(DeviceConnectedEvent evt) => DeviceConnected?.Invoke(this, evt);
    // 设备断开连接：立即发布（无延迟）在RecorderConnectMonitor中发布
    public void PublishDeviceDisconnected(DeviceDisconnectedEvent evt) => DeviceDisconnected?.Invoke(this, evt);

    public void Dispose() => _flushTimer?.Dispose();
}
