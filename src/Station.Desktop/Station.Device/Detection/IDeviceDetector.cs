using Station.Contracts;

namespace Station.Device.Detection;

/// <summary>
/// 单协议设备检测器。
/// 每个协议（UMS / MTP / PrivateSdk）一份实现；平台差异由实现内部处理。
/// 实现须无副作用、可重复调用、快速返回（同步，底层为系统调用）。
/// </summary>
public interface IDeviceDetector
{
    /// <summary>检测器对应的协议。</summary>
    ProtocolType Protocol { get; }

    /// <summary>一次性探测当前接入设备。</summary>
    IReadOnlyList<DetectedDevice> Detect();
}
