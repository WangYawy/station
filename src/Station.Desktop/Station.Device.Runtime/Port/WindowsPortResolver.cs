using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Station.Device.Port;

namespace Station.Device.Runtime.Port;

/// <summary>
/// Windows 物理 USB 端口解析（SetupAPI）。
/// 输出：<c>Win:Hub{XXXX}:Port{XXXX}</c>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPortResolver : IUsbPortResolver
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;
    private const uint SPDRP_ENUMERATOR_NAME = 0x00000016;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    private static readonly Guid GUID_DEVINTERFACE_DISK =
        new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    private readonly ILogger<WindowsPortResolver> _logger;

    public WindowsPortResolver(ILogger<WindowsPortResolver> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable => OperatingSystem.IsWindows();
    public string? UnavailableReason => IsAvailable ? null : "当前平台非 Windows";

    public string? ResolvePhysicalPortId(string deviceRoot)
    {
        if (string.IsNullOrWhiteSpace(deviceRoot)) return null;
        if (!IsAvailable) return null;

        try
        {
            var volumePath = NormalizeVolumePath(deviceRoot);
            if (volumePath is null) return null;

            var deviceNumber = GetVolumeDeviceNumber(volumePath);
            if (deviceNumber is null) return null;

            var location = FindUsbLocationByDeviceNumber(deviceNumber.Value);
            if (location is null) return null;

            return NormalizeLocation(location);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Windows 端口解析失败：{Root}", deviceRoot);
            return null;
        }
    }

    // ---------- 盘符归一 ----------

    private static string? NormalizeVolumePath(string root)
    {
        if (root.Length < 2 || root[1] != ':') return null;
        var mount = root.Substring(0, 3);   // "E:\"
        var sb = new StringBuilder(260);
        return GetVolumeNameForVolumeMountPoint(mount, sb, (uint)sb.Capacity)
            ? sb.ToString()
            : null;
    }

    // ---------- 设备号 ----------

    private static int? GetVolumeDeviceNumber(string volumePath)
    {
        var handle = CreateFile(volumePath, GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
            return null;

        try
        {
            var size = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                if (!DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER,
                        IntPtr.Zero, 0, ptr, (uint)size, out _, IntPtr.Zero))
                    return null;

                var sdn = Marshal.PtrToStructure<STORAGE_DEVICE_NUMBER>(ptr);
                return sdn.DeviceNumber;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ---------- SetupAPI ----------

    private static string? FindUsbLocationByDeviceNumber(int deviceNumber)
    {
        var classGuid = GUID_DEVINTERFACE_DISK;
        var hDevInfo = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == INVALID_HANDLE_VALUE) return null;

        try
        {
            var ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            for (uint i = 0; SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero,
                    ref classGuid, i, ref ifData); i++)
            {
                var detail = GetDeviceInterfaceDetail(hDevInfo, ref ifData);
                if (detail is null) continue;

                var devNumber = GetDeviceNumberFromInterfacePath(detail);
                if (devNumber != deviceNumber) continue;

                return GetDeviceLocation(hDevInfo, ref ifData);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }
        return null;
    }

    private static string? GetDeviceInterfaceDetail(IntPtr hDevInfo,
        ref SP_DEVICE_INTERFACE_DATA ifData)
    {
        SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0,
            out var required, IntPtr.Zero);
        if (required == 0) return null;

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, buffer,
                    required, out _, IntPtr.Zero))
                return null;

            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int? GetDeviceNumberFromInterfacePath(string interfacePath)
    {
        var handle = CreateFile(interfacePath, 0,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
            return null;

        try
        {
            var size = Marshal.SizeOf<STORAGE_DEVICE_NUMBER>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                if (!DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER,
                        IntPtr.Zero, 0, ptr, (uint)size, out _, IntPtr.Zero))
                    return null;

                var sdn = Marshal.PtrToStructure<STORAGE_DEVICE_NUMBER>(ptr);
                return sdn.DeviceNumber;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? GetDeviceLocation(IntPtr hDevInfo,
        ref SP_DEVICE_INTERFACE_DATA ifData)
    {
        var devInfo = new SP_DEVINFO_DATA();
        devInfo.cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>();
        if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0,
                out _, ref devInfo))
            return null;

        // 找 USB 父设备
        var current = devInfo;
        for (var depth = 0; depth < 8; depth++)
        {
            if (CM_Get_Parent(out var parent, current.DevInst, 0) != 0) return null;
            current.DevInst = parent;

            var enumerator = ReadDeviceProperty(hDevInfo, ref current, SPDRP_ENUMERATOR_NAME);
            if (string.Equals(enumerator, "USB", StringComparison.OrdinalIgnoreCase))
                return ReadDeviceProperty(hDevInfo, ref current, SPDRP_LOCATION_INFORMATION);
        }
        return null;
    }

    private static string? ReadDeviceProperty(IntPtr hDevInfo,
        ref SP_DEVINFO_DATA devInfo, uint property)
    {
        var buffer = new byte[512];
        if (!SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfo, property,
                out _, buffer, (uint)buffer.Length, out var size))
            return null;

        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    // ---------- 位置字符串归一 ----------

    private static string? NormalizeLocation(string location)
    {
        // 形如 "Port_#0001.Hub_#0001"
        var parts = location.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var port = parts.FirstOrDefault(p => p.StartsWith("Port_#", StringComparison.OrdinalIgnoreCase));
        var hub = parts.FirstOrDefault(p => p.StartsWith("Hub_#", StringComparison.OrdinalIgnoreCase));
        if (port is null || hub is null) return null;

        var portNum = port.Substring("Port_#".Length).TrimStart('0');
        var hubNum = hub.Substring("Hub_#".Length).TrimStart('0');
        if (portNum.Length == 0) portNum = "0";
        if (hubNum.Length == 0) hubNum = "0";

        return $"Win:Hub{int.Parse(hubNum):D4}:Port{int.Parse(portNum):D4}";
    }

    // ---------- P/Invoke ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_NUMBER
    {
        public int DeviceType;
        public int DeviceNumber;
        public int PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint, StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr hDevInfo, IntPtr devInfo, ref Guid interfaceClassGuid,
        uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr hDevInfo, ref SP_DEVINFO_DATA devInfo, uint property,
        out uint propertyRegDataType, byte[] propertyBuffer,
        uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr hDevInfo);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);
}
