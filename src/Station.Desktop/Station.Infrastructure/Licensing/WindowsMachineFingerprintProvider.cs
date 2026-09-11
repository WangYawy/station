using System.Management;
using Station.Contracts.Registration;
using Station.Infrastructure.Security;

namespace Station.Infrastructure.Licensing;

/// <summary>
/// Windows 机器指纹：CPU/主板/磁盘序列号（WMI）+ MAC，组合后 SM3。
/// </summary>
public sealed class WindowsMachineFingerprintProvider : IMachineFingerprintProvider
{
    public MachineFingerprint CollectParts() => new()
    {
        CpuSerial = QueryFirst("Win32_Processor", "ProcessorId"),
        MotherboardSerial = QueryFirst("Win32_BaseBoard", "SerialNumber"),
        DiskSerial = QueryFirst("Win32_DiskDrive", "SerialNumber"),
        MacAddress = MachineFingerprintUtil.FirstMac()
    };

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

    public string CollectFingerprint() => Sm3Checksum.ComputeString(CollectParts().ToRaw());
}
