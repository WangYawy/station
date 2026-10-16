using System.Text;
using Station.Device.Detection;
using Station.Device.Identity;

namespace Station.Device.Runtime.Identity;

/// <summary>
/// 默认身份归一实现：PhysicalPortId > Serial > Root。
/// 输出统一大写、去空白。
/// </summary>
public sealed class DeviceIdentityResolver : IDeviceIdentityResolver
{
    public string Resolve(DetectedDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.PhysicalPortId))
            return "PORT:" + Normalize(device.PhysicalPortId);

        if (!string.IsNullOrWhiteSpace(device.Serial))
            return "SN:" + Normalize(device.Serial);

        return "ROOT:" + Normalize(device.Root);
    }

    private static string Normalize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
            sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }
}
