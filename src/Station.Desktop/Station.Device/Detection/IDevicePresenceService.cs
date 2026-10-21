namespace Station.Device.Detection;

/// <summary>
/// 设备在线聚合服务：多协议检测 + 稳定判定 + 去重 + 物理事件广播。
/// 生命周期单例，长期运行。
/// </summary>
public interface IDevicePresenceService
{
    /// <summary>当前在线设备快照。</summary>
    IReadOnlyList<DetectedDevice> GetConnectedDevices();

    /// <summary>
    /// 对指定设备重跑识别流程（拒绝后重试用）。
    /// 内部重新发一次 DeviceConnectedEvent，由 Application 层重新走识别。
    /// </summary>
    Task RecheckAsync(string deviceKey, CancellationToken ct = default);

    /// <summary>由宿主在 UI 准备就绪后调用，开始轮询。</summary>
    void BeginPolling();
}
