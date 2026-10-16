namespace Station.Device.Events;

/// <summary>设备物理断开事件。</summary>
public sealed record DeviceDisconnectedEvent(
    string DeviceKey,
    string DeviceName,
    string? PhysicalPortId,
    long Version = 0);
