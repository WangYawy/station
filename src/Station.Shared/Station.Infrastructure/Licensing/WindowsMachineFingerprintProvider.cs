using System.Management;
using System.Net.NetworkInformation;
using Station.Infrastructure.Security;

namespace Station.Infrastructure.Licensing;

/// <summary>
/// Windows 机器指纹：CPU 序列号 + 主板序列号 + 磁盘序列号 + MAC，
/// 组合后 SM3 哈希（换硬件即新指纹）。Linux/信创实现后续补充。
/// </summary>
public sealed class WindowsMachineFingerprintProvider : IMachineFingerprintProvider
{
    public string CollectFingerprint()
    {
        var raw = $"{QueryFirst("Win32_Processor", "ProcessorId")}|" +
                  $"{QueryFirst("Win32_BaseBoard", "SerialNumber")}|" +
                  $"{QueryFirst("Win32_DiskDrive", "SerialNumber")}|" +
                  $"{FirstMac()}";
        return Sm3Checksum.ComputeString(raw);
    }

    private static string QueryFirst(string className, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT {property} FROM {className}");
            foreach (var obj in searcher.Get())
            {
                var value = obj[property]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // 单字段失败不影响整体
        }

        return "unknown";
    }

    private static string FirstMac()
    {
        try
        {
            var mac = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(a => !string.IsNullOrEmpty(a));
            return mac ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
