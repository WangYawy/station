using Station.Domain.Enums;

namespace Station.Application.UsbPortCard.Events;
/// <summary>
/// 采集卡片事件服务
/// </summary>
public interface IUsbPortCardEventService
{
    // 订阅事件
    event EventHandler<TaskProgressUpdatedEvent>? TaskProgressUpdated;
    event EventHandler<TaskStatusChangedEvent>? TaskStatusChanged;
    event EventHandler<DeviceConnectedEvent>? DeviceConnected;
    event EventHandler<DeviceDisconnectedEvent>? DeviceDisconnected;

    /// <summary>
    /// 发布任务采集进度更新事件
    /// </summary>
    void PublishTaskProgress(long taskId, double progress, double speed);

    /// <summary>
    /// 发布任务状态更新事件
    /// </summary>
    void PublishTaskStatus(long taskId, CollectTaskStatus status, bool isEmergency);
    /// <summary>
    /// 发布设备连接事件
    /// </summary>
    void PublishDeviceConnected(DeviceConnectedEvent evt);
    /// <summary>
    /// 发布设备断开连接事件
    /// </summary>
    void PublishDeviceDisconnected(DeviceDisconnectedEvent evt);

    /// <summary>
    /// 发布文件采集进度更新事件
    /// </summary>
    // void PublishFileProgress(long fileId, double progress, double speed);
}
