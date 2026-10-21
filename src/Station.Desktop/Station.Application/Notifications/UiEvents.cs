using Station.Domain.Collecting;

namespace Station.Application.Notifications;

// ============================================================
// 设备维度
// ============================================================
/// <summary>设备识别通过（业务事件）。</summary>
public sealed record DeviceBoundEvent(
    string DeviceKey,
    long UserId,
    long DeptId,
    string? PhysicalPortId,
    string? UserName,
    string? DeptName,
    long Version = 0);

/// <summary>设备识别拒绝（业务事件）。</summary>
public sealed record DeviceRejectedEvent(
    string DeviceKey,
    string DeviceName,
    string Reason,
    string? PhysicalPortId,
    long Version = 0);

// ============================================================
// 任务维度
// ============================================================
/// <summary>任务状态变化事件。</summary>
public sealed record TaskStatusChangedEvent(
    long TaskId, CollectTaskStatus Status, bool IsEmergency,
    string? PhysicalPortId = null, long Version = 0);

/// <summary>任务进度事件。</summary>
public sealed record TaskProgressUpdatedEvent(
    long TaskId, double Progress, double SpeedBytesPerSecond,
    string? PhysicalPortId = null, long Version = 0);

/// <summary>任务文件完成事件。</summary>
public sealed record TaskFileCompletedEvent(
    long TaskId, long FileId, CollectFileStatus Status);
