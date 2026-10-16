using Microsoft.Extensions.Options;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.Options;

namespace Station.Device.Runtime.Detection;

/// <summary>
/// UMS（U 盘模式）检测器。
/// 支持 <see cref="DeviceDetectionOptions.UmsRootOverride"/> 用于开发 / 测试。
/// </summary>
public sealed class UmsDetector : IDeviceDetector
{
    private readonly DeviceDetectionOptions _options;

    public UmsDetector(IOptions<DeviceDetectionOptions> options)
    {
        _options = options.Value;
    }

    public ProtocolType Protocol => ProtocolType.Ums;

    public IReadOnlyList<DetectedDevice> Detect()
    {
        // 开发 / 测试：直接指定根路径
        if (!string.IsNullOrWhiteSpace(_options.UmsRootOverride))
        {
            var root = _options.UmsRootOverride!;
            if (!Directory.Exists(root)) return Array.Empty<DetectedDevice>();

            var name = new DirectoryInfo(root).Name;
            return new[]
            {
                new DetectedDevice(
                    Key: root,                             // 稍后由身份归一覆盖
                    Name: name,
                    Serial: null,
                    Protocol: ProtocolType.Ums,
                    Root: root)
            };
        }

        // 真实环境：枚举可移动磁盘
        var list = new List<DetectedDevice>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Removable) continue;
                if (!drive.IsReady) continue;

                var root = drive.RootDirectory.FullName;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : drive.VolumeLabel;

                list.Add(new DetectedDevice(
                    Key: root,
                    Name: label,
                    Serial: null,
                    Protocol: ProtocolType.Ums,
                    Root: root));
            }
            catch
            {
                // 单个盘符访问异常不影响其他
            }
        }
        return list;
    }
}
