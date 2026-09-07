
using System.Runtime.Versioning;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Application.DeviceDetection;
using Station.Infrastructure.Collecting;
using Vanara.PInvoke;

namespace Station.Infrastructure.DeviceDetection;
/// <summary>MTP 检测：Windows 便携设备 API（WPD），Linux libmtp。</summary>
[SupportedOSPlatform("windows")]
public sealed class MtpDeviceDetector : IRecorderDeviceDetector
{
    private readonly CollectOptions _options;

    public MtpDeviceDetector(CollectOptions options)
    {
        _options = options;
    }

    public ProtocolType Protocol => ProtocolType.Mtp;

    public IReadOnlyList<DetectedDevice> Detect() => DetectWindows();
       

    private static IReadOnlyList<DetectedDevice> DetectWindows()
    {
        var list = new List<DetectedDevice>();
        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
        var deviceIds = manager.GetDevices(forceRefresh: false);
        foreach (var id in deviceIds)
        {
            string friendly;
            try
            {
                friendly = manager.GetDeviceFriendlyName(id) ?? string.Empty;
            }
            catch
            {
                friendly = string.Empty;
            }

            list.Add(new DetectedDevice(
                "MTP:" + id,
                string.IsNullOrWhiteSpace(friendly) ? id : friendly,
                null,
                ProtocolType.Mtp,
                MtpRoot.RootScheme + id));
        }

        return list;
    }
}
