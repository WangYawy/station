
using Station.Contracts;

namespace Station.Application.DeviceDetection;
/// <summary>检测到的记录仪接入设备（UMS 盘符 / MTP 便携设备）。</summary>
public sealed record DetectedDevice(
    string Key,
    string Name,
    string? Serial,
    ProtocolType Protocol,
    string Root);

/// <summary>设备接入检测器：按采集源模式返回当前在线设备。</summary>
public interface IRecorderDeviceDetector
{
    /// <summary>连接类型</summary>
    ProtocolType Protocol { get; }
    /// <summary>
    /// 检测已连接设备
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<DetectedDevice> Detect();
}
