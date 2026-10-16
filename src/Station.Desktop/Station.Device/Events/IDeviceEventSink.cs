namespace Station.Device.Events;

/// <summary>
/// 设备物理事件出口。
/// 只承载物理层事件（接入 / 断开），业务事件（Bound / Rejected）由 Application 层自行广播。
/// </summary>
public interface IDeviceEventSink
{
    /// <summary>物理接入：探测稳定后立即发。</summary>
    void PublishConnected(DeviceConnectedEvent e);

    /// <summary>物理断开：从在线集合移除后发。</summary>
    void PublishDisconnected(DeviceDisconnectedEvent e);
}
