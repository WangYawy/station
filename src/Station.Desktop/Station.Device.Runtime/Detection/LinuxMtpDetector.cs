using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.Options;
using Station.Device.Runtime.Interop;

namespace Station.Device.Runtime.Detection;

/// <summary>
/// Linux / Kylin / UOS MTP 检测器：libmtp。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMtpDetector : IDeviceDetector
{
    private readonly DeviceDetectionOptions _options;

    public LinuxMtpDetector(IOptions<DeviceDetectionOptions> options)
    {
        _options = options.Value;
    }

    public ProtocolType Protocol => ProtocolType.Mtp;

    public IReadOnlyList<DetectedDevice> Detect()
    {
        if (!OperatingSystem.IsLinux() || !LibMtpInterop.IsAvailable)
            return Array.Empty<DetectedDevice>();

        try
        {
            LibMtpInterop.LIBMTP_Init();
            IntPtr list = IntPtr.Zero;
            var err = LibMtpInterop.LIBMTP_Get_Connected_Devices(ref list);
            if (err != (int)LibMtpInterop.Error.None || list == IntPtr.Zero)
                return Array.Empty<DetectedDevice>();

            var result = new List<DetectedDevice>();
            try
            {
                for (var node = list; node != IntPtr.Zero; node = LibMtpInterop.DeviceNext(node))
                {
                    var key = LibMtpInterop.GetDeviceKey(node);
                    var friendly = LibMtpInterop.TakeString(LibMtpInterop.LIBMTP_Get_Friendlyname(node))
                                   ?? LibMtpInterop.TakeString(LibMtpInterop.LIBMTP_Get_Modelname(node))
                                   ?? key;

                    if (!string.IsNullOrWhiteSpace(_options.UmsRootOverride)
                        && friendly.Contains(_options.UmsRootOverride, StringComparison.OrdinalIgnoreCase))
                        continue;

                    result.Add(new DetectedDevice(
                        Key: "MTP:" + key,                              // 归一前占位
                        Name: friendly,
                        Serial: null,
                        Protocol: ProtocolType.Mtp,
                        Root: LibMtpInterop.RootScheme + key));
                }
            }
            finally
            {
                LibMtpInterop.LIBMTP_Release_Device_List(list);
            }
            return result;
        }
        catch
        {
            return Array.Empty<DetectedDevice>();
        }
    }
}
