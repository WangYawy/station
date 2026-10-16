using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vanara.PInvoke;

namespace Station.Device.Runtime.Interop;

/// <summary>
/// Windows 便携设备 API（WPD）共享辅助。
/// Detector / FileSystem 共用 WPD 会话与常用操作。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WpdInterop
{
    internal const char PartSeparator = '\u001F';
    internal const char PathSeparator = '/';
    internal const string RootScheme = "MTP://";

    /// <summary>枚举当前系统接入的 MTP 设备 ID 列表。</summary>
    internal static string[] ListDeviceIds()
    {
        var manager = (PortableDeviceApi.IPortableDeviceManager)
            new PortableDeviceApi.PortableDeviceManager();
        manager.RefreshDeviceList();
        return manager.GetDevices(forceRefresh: false);
    }

    /// <summary>取设备友好名；异常返回 null。</summary>
    internal static string? GetFriendlyName(string deviceId)
    {
        try
        {
            var manager = (PortableDeviceApi.IPortableDeviceManager)
                new PortableDeviceApi.PortableDeviceManager();
            return manager.GetDeviceFriendlyName(deviceId);
        }
        catch { return null; }
    }
}
