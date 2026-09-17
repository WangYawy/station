using System;
using System.Collections.Concurrent;
using Station.Domain.Enums;

namespace Station.Application.UsbPortCard.Events;
public sealed class UsbPortCardEventService : IUsbPortCardEventService
{
    // 标准事件委托
    public event EventHandler<TaskProgressUpdatedEvent>? TaskProgressUpdated;
    public event EventHandler<TaskStatusChangedEvent>? TaskStatusChanged;
    public event EventHandler<DeviceConnectedEvent>? DeviceConnected;
    public event EventHandler<DeviceDisconnectedEvent>? DeviceDisconnected;
    public event EventHandler<DeviceBoundEvent>? DeviceBound;
    public event EventHandler<DeviceRejectedEvent>? DeviceRejected;

    public UsbPortCardEventService()
    {
    }

    // 进度：直接派发，由订阅方按帧合流
    public void PublishTaskProgress(long taskId, double progress, double speed)
    {
        TaskProgressUpdated?.Invoke(this, new TaskProgressUpdatedEvent(taskId, progress, speed));
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

    // 绑定验证成功，立即发布
    public void PublishDeviceBound(DeviceBoundEvent evt) => DeviceBound?.Invoke(this, evt);
    // 绑定验证失败，立即发布
    public void PublishDeviceRejected(DeviceRejectedEvent evt) => DeviceRejected?.Invoke(this, evt);

}
