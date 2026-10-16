using Station.Contracts;

namespace Station.Device.Events;

/// <summary>设备物理接入事件（探测到即发，识别前）。</summary>
public sealed record DeviceConnectedEvent(
    string DeviceKey,
    string DeviceName,
    ProtocolType Protocol,
    string Root,
    string? PhysicalPortId,
    long Version = 0);
