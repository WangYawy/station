using Station.Application.Collecting;
using Station.Contracts;
using Station.Application.DeviceDetection;

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

    public IReadOnlyList<DetectedDevice> Detect()
    {
        if (!string.IsNullOrWhiteSpace(_options.UmsRootOverride))
        {
            return
            [
                new DetectedDevice(
                    "UMS:override",
                    Path.GetFileName(_options.UmsRootOverride.TrimEnd(Path.DirectorySeparatorChar)),
                    null,
                    ProtocolType.Ums,
                    _options.UmsRootOverride)
            ];
        }

        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
            .Select(d => new DetectedDevice(
                "UMS:" + d.RootDirectory.FullName,
                string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.RootDirectory.FullName : d.VolumeLabel,
                null,
                ProtocolType.Ums,
                d.RootDirectory.FullName))
            .ToList();
    }
}
