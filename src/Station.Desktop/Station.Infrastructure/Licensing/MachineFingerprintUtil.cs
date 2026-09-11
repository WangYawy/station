using System.Net.NetworkInformation;

namespace Station.Infrastructure.Licensing;

internal static class MachineFingerprintUtil
{
    public static string FirstMac()
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
