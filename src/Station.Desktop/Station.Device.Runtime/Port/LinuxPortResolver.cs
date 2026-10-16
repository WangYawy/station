using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Station.Device.Port;

namespace Station.Device.Runtime.Port;

/// <summary>
/// Linux / Kylin / UOS 物理 USB 端口解析（sysfs 主通道）。
/// 输出：<c>Lin:{bus}-{hub}.{port}</c>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxPortResolver : IUsbPortResolver
{
    private const string SysUsbDevices = "/sys/bus/usb/devices";
    private const string SysBlock = "/sys/class/block";
    private const string ProcMounts = "/proc/mounts";

    private readonly ILogger<LinuxPortResolver> _logger;

    public LinuxPortResolver(ILogger<LinuxPortResolver> logger)
    {
        _logger = logger;
        IsAvailable = CheckAvailability(out var reason);
        UnavailableReason = reason;
    }

    public bool IsAvailable { get; }
    public string? UnavailableReason { get; }

    public string? ResolvePhysicalPortId(string deviceRoot)
    {
        if (string.IsNullOrWhiteSpace(deviceRoot)) return null;
        if (!IsAvailable) return null;

        try
        {
            var portId = ResolveFromSysFs(deviceRoot);
            return portId is null ? null : "Lin:" + portId;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "sysfs 无权限读取，无法解析物理端口：{Root}", deviceRoot);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Linux 端口解析失败：{Root}", deviceRoot);
            return null;
        }
    }

    // ---------- 可用性 ----------

    private static bool CheckAvailability(out string? reason)
    {
        if (!OperatingSystem.IsLinux())
        {
            reason = "当前平台非 Linux";
            return false;
        }
        if (!Directory.Exists(SysUsbDevices))
        {
            reason = "sysfs 未挂载，请检查 /sys/bus/usb/devices";
            return false;
        }
        try
        {
            _ = Directory.EnumerateDirectories(SysUsbDevices).FirstOrDefault();
            reason = null;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            reason = "sysfs 无权限访问，请以管理员运行或调整权限";
            return false;
        }
        catch (Exception ex)
        {
            reason = $"sysfs 访问异常：{ex.Message}";
            return false;
        }
    }

    // ---------- sysfs 解析 ----------

    private static string? ResolveFromSysFs(string deviceRoot)
    {
        var blockName = FindBlockDeviceByMountPoint(deviceRoot);
        if (blockName is null) return null;

        var link = Path.Combine(SysBlock, blockName, "device");
        if (!Directory.Exists(link) && !File.Exists(link)) return null;

        var target = File.ResolveLinkTarget(link, returnFinalTarget: true);
        if (target is null) return null;

        return ExtractUsbPortFromSysPath(target.FullName);
    }

    private static string? FindBlockDeviceByMountPoint(string mountPoint)
    {
        if (!File.Exists(ProcMounts)) return null;
        var realMount = Path.GetFullPath(mountPoint).TrimEnd('/');

        foreach (var line in File.ReadLines(ProcMounts))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var mount = parts[1];
            if (!string.Equals(mount.TrimEnd('/'), realMount, StringComparison.Ordinal))
                continue;

            var name = Path.GetFileName(parts[0]);
            if (string.IsNullOrEmpty(name)) continue;
            return StripPartitionSuffix(name);
        }
        return null;
    }

    private static string StripPartitionSuffix(string name)
    {
        // sdb1 → sdb；nvme0n1p1 → nvme0n1
        var i = name.Length - 1;
        while (i >= 0 && char.IsDigit(name[i])) i--;
        return i < name.Length - 1 ? name.Substring(0, i + 1) : name;
    }

    private static string? ExtractUsbPortFromSysPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var s = segments[i];
            if (s.Contains('-') && IsUsbPortFormat(s))
                return s;
        }
        return null;
    }

    private static bool IsUsbPortFormat(string s)
    {
        foreach (var c in s)
            if (!char.IsDigit(c) && c != '-' && c != '.') return false;
        return s.Length > 0 && s.Contains('-');
    }
}
