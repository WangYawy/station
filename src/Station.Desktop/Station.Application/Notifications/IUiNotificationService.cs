using Station.Device.Events;
using Station.Domain.Collecting;

namespace Station.Application.Notifications;

/// <summary>UI 通知服务：领域无关的即时界面刷新。</summary>
public interface IUiNotificationService
{
    // ---------- 物理事件（来自 Device 层） ----------
    event EventHandler<DeviceConnectedEvent>? DeviceConnected;
    event EventHandler<DeviceDisconnectedEvent>? DeviceDisconnected;
    event EventHandler<DeviceBoundEvent>? DeviceBound;
    event EventHandler<DeviceRejectedEvent>? DeviceRejected;

    // ---------- 任务事件 ----------
    event EventHandler<TaskStatusChangedEvent>? TaskStatusChanged;
    event EventHandler<TaskProgressUpdatedEvent>? TaskProgressUpdated;
    event EventHandler<TaskFileCompletedEvent>? TaskFileCompleted;

    // ---------- 发布 ----------
    void PublishDeviceConnected(DeviceConnectedEvent e);
    void PublishDeviceDisconnected(DeviceDisconnectedEvent e);
    void PublishDeviceBound(DeviceBoundEvent e);
    void PublishDeviceRejected(DeviceRejectedEvent e);

    void PublishTaskStatus(long taskId, CollectTaskStatus status, bool isEmergency,
                           string? physicalPortId = null, long version = 0);
    void PublishTaskProgress(long taskId, double progress, double speedBytesPerSecond,
                             string? physicalPortId = null, long version = 0);
    void PublishTaskFileCompleted(long taskId, long fileId, CollectFileStatus status);
}


