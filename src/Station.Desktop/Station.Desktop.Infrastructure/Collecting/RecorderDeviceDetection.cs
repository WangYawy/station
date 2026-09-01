using System.Runtime.Versioning;
using Station.Application.Collecting;
using Station.Contracts;
using Vanara.PInvoke;

namespace Station.Desktop.Infrastructure.Collecting;

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
    /// <summary>匹配 CollectOptions.SourceMode（ums / mtp）。</summary>
    string Protocol { get; }
    /// <summary>
    /// 发现设备
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<DetectedDevice> Detect();
}

/// <summary>UMS（U 盘模式）检测：可移动磁盘；配置 UmsRootOverride 时视为固定设备（开发/测试）。</summary>
public sealed class UmsDeviceDetector : IRecorderDeviceDetector
{
    private readonly CollectOptions _options;

    public UmsDeviceDetector(CollectOptions options)
    {
        _options = options;
    }

    public string Protocol => "ums";

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

/// <summary>MTP 检测：Windows 便携设备 API（WPD），Linux libmtp。</summary>
public sealed class MtpDeviceDetector : IRecorderDeviceDetector
{
    private readonly CollectOptions _options;

    public MtpDeviceDetector(CollectOptions options)
    {
        _options = options;
    }

    public string Protocol => "mtp";

    public IReadOnlyList<DetectedDevice> Detect() =>
        OperatingSystem.IsWindows()
            ? DetectWindows()
            : OperatingSystem.IsLinux()
                ? LinuxMtpCollectSource.DetectDevices(_options)
                : [];

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<DetectedDevice> DetectWindows()
    {
        var list = new List<DetectedDevice>();
        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
        var deviceIds = PortableDeviceApi.GetDevices(manager, forceRefresh: false);
        foreach (var id in deviceIds)
        {
            string friendly;
            try
            {
                friendly = PortableDeviceApi.GetDeviceFriendlyName(manager, id) ?? string.Empty;
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
