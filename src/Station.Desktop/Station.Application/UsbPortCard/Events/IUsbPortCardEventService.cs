using Station.Domain.Enums;

namespace Station.Application.UsbPortCard.Events;

/// <summary>
/// USB 端口卡片事件总线（纯发布/订阅，不做内部节流）。
/// 节流/合帧职责由订阅方（如 WorkbenchViewModel）自行决定。
/// </summary>
public interface IUsbPortCardEventService
{
    // 物理事件
    event EventHandler<DeviceConnectedEvent> DeviceConnected;
    event EventHandler<DeviceDisconnectedEvent> DeviceDisconnected; 

    // 业务事件
    event EventHandler<DeviceBoundEvent> DeviceBound;
    event EventHandler<DeviceRejectedEvent> DeviceRejected;

    // 任务事件
    event EventHandler<TaskProgressUpdatedEvent> TaskProgressUpdated;
    event EventHandler<TaskStatusChangedEvent> TaskStatusChanged;


    /// <summary>
    /// 物理设备已插入连接事件发布
    /// </summary>
    void PublishDeviceConnected(DeviceConnectedEvent evt);
    /// <summary>
    /// 发布设备断开连接事件
    /// </summary>
    void PublishDeviceDisconnected(DeviceDisconnectedEvent evt);

    /// <summary>
    /// 发布绑定验证成功事件
    /// </summary>
    /// <param name="evt"></param>
    void PublishDeviceBound(DeviceBoundEvent evt);
    /// <summary>
    /// 发布绑定验证失败事件
    /// </summary>
    /// <param name="evt"></param>
    void PublishDeviceRejected(DeviceRejectedEvent evt);

    /// <summary>
    /// 发布任务采集进度更新事件
    /// </summary>
    void PublishTaskProgress(long taskId, double progress, double speed);

    /// <summary>
    /// 发布任务状态更新事件
    /// </summary>
    void PublishTaskStatus(long taskId, CollectTaskStatus status, bool isEmergency);


    /// <summary>
    /// 发布文件采集进度更新事件
    /// </summary>
    // void PublishFileProgress(long fileId, double progress, double speed);
}
