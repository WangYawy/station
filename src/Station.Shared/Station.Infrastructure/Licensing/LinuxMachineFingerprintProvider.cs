using Station.Contracts.Registration;

namespace Station.Infrastructure.Licensing;

/// <summary>
/// Linux/信创 机器指纹：DMI（CPU 用 product_uuid、主板用 board_serial）+ 磁盘序列号 + MAC。
/// 读取 /sys/class/dmi/id 与 /sys/block/*/device/serial；无权限/缺失时回退 "unknown"。
/// </summary>
public sealed class LinuxMachineFingerprintProvider : IMachineFingerprintProvider
{
    private readonly string _sysfsRoot;

    public LinuxMachineFingerprintProvider(string? sysfsRoot = null)
    {
        _sysfsRoot = sysfsRoot ?? "/sys";
    }

    public MachineFingerprint CollectParts() => new()
    {
        CpuSerial = ReadFirst(
            $"{_sysfsRoot}/class/dmi/id/product_uuid",
            $"{_sysfsRoot}/class/dmi/id/product_name"),
        MotherboardSerial = Read($"{_sysfsRoot}/class/dmi/id/board_serial"),
        DiskSerial = FindDiskSerial(),
        MacAddress = MachineFingerprintUtil.FirstMac()
    };

    private string FindDiskSerial()
    {
        var blocks = Path.Combine(_sysfsRoot, "block");
        if (!Directory.Exists(blocks))
        {
            return "unknown";
        }

        foreach (var dir in Directory.GetDirectories(blocks).OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("loop", StringComparison.Ordinal) ||
                name.StartsWith("ram", StringComparison.Ordinal) ||
                name.StartsWith("sr", StringComparison.Ordinal))
            {
                continue;
            }

            var serial = ReadFirst(
                Path.Combine(dir, "device", "serial"),
                Path.Combine(dir, "serial"));
            if (serial != "unknown")
            {
                return serial;
            }
        }

        return "unknown";
    }

    private static string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ReadFirst(params string[] paths)
    {
        foreach (var path in paths)
        {
            var value = Read(path);
            if (value != "unknown")
            {
                return value;
            }
        }

        return "unknown";
    }
}
