using System.Management;
using System.Text;
using Station.Application.Licensing;
using Station.Contracts.Registration;
using Station.Crypto.Providers.Hashers;

namespace Station.Infrastructure.Licensing;

/// <summary>
/// Windows 机器指纹：CPU/主板/磁盘序列号（WMI）+ MAC，组合后 SM3。
/// 
/// 【算法固定】机器指纹固定使用 SM3，不读策略表。
///   原因：指纹是机器身份，算法变更会导致所有授权失效。
/// </summary>
public sealed class WindowsMachineFingerprintProvider : IMachineFingerprintProvider
{
    private readonly Sm3Hasher _hasher = new();

    public MachineFingerprint CollectParts() => new()
    {
        CpuSerial = QueryFirst("Win32_Processor", "ProcessorId"),
        MotherboardSerial = QueryFirst("Win32_BaseBoard", "SerialNumber"),
        DiskSerial = QueryFirst("Win32_DiskDrive", "SerialNumber"),
        MacAddress = MachineFingerprintUtil.FirstMac()
    };

    public string CollectFingerprint() =>
        _hasher.ComputeHash(Encoding.UTF8.GetBytes(CollectParts().ToRaw()));

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
                    return value;
            }
        }
        catch
        {
            // 单字段失败不影响整体
        }

        return "unknown";
    }
}
