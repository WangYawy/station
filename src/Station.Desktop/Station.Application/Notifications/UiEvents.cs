using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Application.Notifications;

// ============================================================
// 设备维度
// ============================================================

public sealed record DeviceConnectedEvent(
    string DeviceKey,
    string DeviceName,
    ProtocolType Protocol,
    string Root);

public sealed record DeviceDisconnectedEvent(
    string DeviceKey,
    string DeviceName);

public sealed record DeviceBoundEvent(
    string DeviceKey,
    long UserId,
    long DeptId);

public sealed record DeviceRejectedEvent(
    string DeviceKey,
    string DeviceName,
    string Reason);

// ============================================================
// 任务维度
// ============================================================

public sealed record TaskStatusChangedEvent(
    long TaskId,
    CollectTaskStatus Status,
    bool IsEmergency);

public sealed record TaskProgressUpdatedEvent(
    long TaskId,
    double Progress,
    double SpeedBytesPerSecond);

public sealed record TaskFileCompletedEvent(
    long TaskId,
    long FileId,
    CollectFileStatus Status);
