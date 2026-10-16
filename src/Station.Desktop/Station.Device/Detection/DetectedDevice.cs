using Station.Contracts;

namespace Station.Device.Detection;

/// <summary>
/// 探测到的记录仪设备（纯物理描述）。
/// 不可变 record，可安全并发传递。
/// 不含任何业务归属字段（User / Dept / Recorder）。
/// </summary>
/// <remarks>
/// <see cref="LinkState"/> 由 Device 层维护，取值仅 Offline / Verifying 两种；
/// Bound / Rejected 属业务态，由 Application 层通过 UI 事件驱动卡片展示。
/// </remarks>
public sealed record DetectedDevice(
    string Key,
    string Name,
    string? Serial,
    ProtocolType Protocol,
    string Root,
    string? PhysicalPortId = null,
    DeviceLinkState LinkState = DeviceLinkState.Offline);
