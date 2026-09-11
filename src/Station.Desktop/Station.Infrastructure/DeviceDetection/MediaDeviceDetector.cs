
using System.Runtime.Versioning;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Application.DeviceDetection;
using Station.Infrastructure.Collecting;
using Vanara.PInvoke;

namespace Station.Infrastructure.DeviceDetection;
/// <summary>MTP 检测：Windows 便携设备 MediaDevices </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaDeviceDetector : IRecorderDeviceDetector
{
    private readonly CollectOptions _options;

    public MediaDeviceDetector(CollectOptions options)
    {
        _options = options;
    }

    public ProtocolType Protocol => ProtocolType.Mtp;

    public IReadOnlyList<DetectedDevice> Detect() => MediaCollectSource.DetectDevices(_options);

}
