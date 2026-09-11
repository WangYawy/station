using Station.Contracts;
using Station.Domain.Enums;

namespace Station.Application.UsbPortCard.Events;

/// <summary>
/// 基类，用于处理USB端口连接设备任务状态变更处理
/// </summary>
public abstract record UsbPortCardEvent;




/// <summary>物理设备已插入（UI 即时响应，不包含业务信息）</summary>
public sealed record DeviceConnectedEvent(
    string DeviceKey,
    string DeviceName,
    ProtocolType Protocol,
    string Root
) : UsbPortCardEvent;

/// <summary>
/// 设备断开连接事件
/// </summary>
/// <param name="DeviceKey"></param>
/// <param name="DeviceName"></param>
public sealed record DeviceDisconnectedEvent(
    string DeviceKey,
    string DeviceName
) : UsbPortCardEvent;

/// <summary>绑定验证成功（包含用户/部门信息，可启动采集）</summary>
public sealed record DeviceBoundEvent(
    string DeviceKey,
    long UserId,
    long DeptId
) : UsbPortCardEvent;

/// <summary>绑定验证失败（用于 UI 展示错误）</summary>
public sealed record DeviceRejectedEvent(
    string DeviceKey,
    string DeviceName,
    string Reason  // "未绑定" / "签名篡改" / "文件损坏" 等
) : UsbPortCardEvent;

/// <summary>
/// 采集任务进度更新事件
/// </summary>
/// <param name="TaskId"></param>
/// <param name="Progress"></param>
/// <param name="SpeedBytesPerSecond"></param>
public sealed record TaskProgressUpdatedEvent(
    long TaskId,
    double Progress,      // 0~100
    double SpeedBytesPerSecond
) : UsbPortCardEvent;

/// <summary>
/// 采集任务状态更新事件
/// </summary>
/// <param name="TaskId"></param>
/// <param name="Status"></param>
/// <param name="IsEmergency"></param>
public sealed record TaskStatusChangedEvent(
    long TaskId,
    CollectTaskStatus Status,
    bool IsEmergency
) : UsbPortCardEvent;
