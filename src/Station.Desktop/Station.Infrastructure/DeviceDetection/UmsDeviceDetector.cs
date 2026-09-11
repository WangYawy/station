using Station.Application.Collecting;
using Station.Contracts;
using Station.Application.DeviceDetection;
using Station.Infrastructure.Collecting;

namespace Station.Infrastructure.DeviceDetection;

/// <summary>UMS（U 盘模式）检测：可移动磁盘；配置 UmsRootOverride 时视为固定设备（开发/测试）。</summary>
public sealed class UmsDeviceDetector : IRecorderDeviceDetector
{
    private readonly CollectOptions _options;

    public UmsDeviceDetector(CollectOptions options)
    {
        _options = options;
    }

    public ProtocolType Protocol => ProtocolType.Ums;

    public IReadOnlyList<DetectedDevice> Detect() => UmsCollectSource.DetectDevices(_options);
}
