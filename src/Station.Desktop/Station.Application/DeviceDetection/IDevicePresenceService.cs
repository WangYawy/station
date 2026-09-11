using Station.Application.DeviceDetection;

namespace Station.Application.DeviceDetection;

/// <summary>
/// 设备在线服务
/// </summary>
public interface IDevicePresenceService
{
    /// <summary>
    /// 获取在线设备
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<DetectedDevice> GetConnectedDevices();
}
