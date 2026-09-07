
using System.Runtime.Versioning;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Application.DeviceDetection;
using Station.Infrastructure.Collecting;
using Vanara.PInvoke;

namespace Station.Infrastructure.DeviceDetection;
/// <summary>MTP 检测：Linux libmtp。</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMtpDeviceDetector : IRecorderDeviceDetector
{
    private readonly CollectOptions _options;

    public LinuxMtpDeviceDetector(CollectOptions options)
    {
        _options = options;
    }

    public ProtocolType Protocol => ProtocolType.Mtp;

    public IReadOnlyList<DetectedDevice> Detect() => LinuxMtpCollectSource.DetectDevices(_options);

}
