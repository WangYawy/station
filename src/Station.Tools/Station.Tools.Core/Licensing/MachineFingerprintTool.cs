using System.Net.NetworkInformation;
using System.Text;
using Station.Crypto;
using Station.Crypto.Engine.Internal;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Licensing;

/// <summary>机器指纹工具。</summary>
public static class MachineFingerprintTool
{
    /// <summary>计算本机指纹（SM3，Base64）。</summary>
    public static ToolResult<string> Collect()
    {
        try
        {
            var parts = new List<string>();

            if (OperatingSystem.IsWindows())
            {
                parts.Add(QueryWmi("Win32_Processor", "ProcessorId"));
                parts.Add(QueryWmi("Win32_BaseBoard", "SerialNumber"));
                parts.Add(QueryWmi("Win32_DiskDrive", "SerialNumber"));
            }
            else if (OperatingSystem.IsLinux())
            {
                parts.Add(ReadLinuxFile("/sys/class/dmi/id/product_uuid"));
                parts.Add(ReadLinuxFile("/sys/class/dmi/id/board_serial"));
            }

            parts.Add(FirstMac());

            var raw = string.Join("|", parts);
            var hasher = AlgorithmRegistry.Default.GetHasher(CryptoAlgorithm.Sm3);
            return ToolResult<string>.Ok(hasher.ComputeHash(Encoding.UTF8.GetBytes(raw)));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("FINGERPRINT_FAIL", ex.Message);
        }
    }

    private static string QueryWmi(string className, string property)
    {
        try
        {
            var type = Type.GetType("System.Management.ManagementObjectSearcher, System.Management");
            if (type is null) return "unknown";

            dynamic? searcher = Activator.CreateInstance(type, $"SELECT {property} FROM {className}");
            if (searcher is null) return "unknown";

            dynamic results = searcher.Get();
            foreach (var obj in results)
            {
                var value = obj[property]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(value)) return value;
            }
        }
        catch { }
        return "unknown";
    }

    private static string ReadLinuxFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown"; }
        catch { return "unknown"; }
    }

    private static string FirstMac()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(ni => ni.GetPhysicalAddress().ToString())
                .FirstOrDefault(addr => !string.IsNullOrEmpty(addr))
                ?? "unknown";
        }
        catch { return "unknown"; }
    }
}
