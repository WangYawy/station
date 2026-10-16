using Station.Domain.Collecting;

namespace Station.Application.Notifications;

/// <summary>
/// UI 通知服务（进程内，同步派发）。
///
/// 【与领域事件的区别】
///   - IDomainEventBus：业务语义事件，异步、可靠，驱动审计/同步/进度聚合
///   - IUiNotificationService：UI 语义通知，同步、即时，驱动界面刷新
///
/// 【使用原则】
///   - 只承载"界面需要知道的事"，不承载业务语义
///   - 事件处理器应尽量轻量，最好只做内存属性更新
///   - 触发方负责在合适时机调用 Publish*；订阅方通过 += 注册
/// </summary>
public interface IUiNotificationService
{
    // =========================================================
    // 设备维度
    // =========================================================

    event EventHandler<DeviceConnectedEvent>? DeviceConnected;
    event EventHandler<DeviceDisconnectedEvent>? DeviceDisconnected;
    event EventHandler<DeviceBoundEvent>? DeviceBound;
    event EventHandler<DeviceRejectedEvent>? DeviceRejected;

    void PublishDeviceConnected(DeviceConnectedEvent e);
    void PublishDeviceDisconnected(DeviceDisconnectedEvent e);
    void PublishDeviceBound(DeviceBoundEvent e);
    void PublishDeviceRejected(DeviceRejectedEvent e);

    // =========================================================
    // 任务维度
    // =========================================================

    event EventHandler<TaskStatusChangedEvent>? TaskStatusChanged;
    event EventHandler<TaskProgressUpdatedEvent>? TaskProgressUpdated;
    event EventHandler<TaskFileCompletedEvent>? TaskFileCompleted;

    void PublishTaskStatus(long taskId, CollectTaskStatus status, bool isEmergency);
    void PublishTaskProgress(long taskId, double progress, double speedBytesPerSecond);
    void PublishTaskFileCompleted(long taskId, long fileId, CollectFileStatus status);
}
