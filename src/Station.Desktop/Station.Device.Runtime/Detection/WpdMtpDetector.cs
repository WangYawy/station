using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.Options;
using Station.Device.Runtime.Interop;

namespace Station.Device.Runtime.Detection;

/// <summary>
/// Windows MTP 检测器：便携设备 API（WPD）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WpdMtpDetector : IDeviceDetector
{
    private readonly DeviceDetectionOptions _options;

    public WpdMtpDetector(IOptions<DeviceDetectionOptions> options)
    {
        _options = options.Value;
    }

    public ProtocolType Protocol => ProtocolType.Mtp;

    public IReadOnlyList<DetectedDevice> Detect()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<DetectedDevice>();

        var result = new List<DetectedDevice>();
        try
        {
            var ids = WpdInterop.ListDeviceIds();
            foreach (var id in ids)
            {
                var friendly = WpdInterop.GetFriendlyName(id) ?? id;

                if (!string.IsNullOrWhiteSpace(_options.UmsRootOverride)
                    && friendly.Contains(_options.UmsRootOverride, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new DetectedDevice(
                    Key: "MTP:" + id,
                    Name: friendly,
                    Serial: null,
                    Protocol: ProtocolType.Mtp,
                    Root: WpdInterop.RootScheme + id));
            }
        }
        catch
        {
            // 探测异常返回空列表，不影响其他协议
        }
        return result;
    }
}
