using System.Runtime.InteropServices;

namespace Station.Device.Runtime.Interop;

/// <summary>
/// libmtp P/Invoke 共享封装。
/// Detector 与 FileSystem 共用，避免重复声明。
/// </summary>
internal static class LibMtpInterop
{
    internal const char PartSeparator = '\u001F';
    internal const char PathSeparator = '/';
    internal const uint FilesAndFoldersRoot = 0xFFFFFFFF;
    internal const string RootScheme = "MTP://";

    // ---------- 枚举 ----------

    internal enum Error { None = 0, General = 1, NoDeviceAttached = 2, Connecting = 3 }

    internal enum FileType { Folder = 0, Text = 28, Unknown = 46 }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MtpDevice
    {
        public byte ObjectBitsize;
        public IntPtr Params;
        public IntPtr UsbInfo;
        public IntPtr Storage;
        public IntPtr ErrorStack;
        public byte MaximumBatteryLevel;
        public uint DefaultMusicFolder;
        public uint DefaultPlaylistFolder;
        public uint DefaultPictureFolder;
        public uint DefaultVideoFolder;
        public uint DefaultOrganizerFolder;
        public uint DefaultZencastFolder;
        public uint DefaultAlbumFolder;
        public uint DefaultTextFolder;
        public IntPtr Cd;
        public IntPtr Extensions;
        public int Cached;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MtpStorage
    {
        public uint Id;
        public ushort StorageType;
        public ushort FilesystemType;
        public ushort AccessCapability;
        public ulong MaxCapacity;
        public ulong FreeSpaceInBytes;
        public ulong FreeSpaceInObjects;
        public IntPtr StorageDescription;
        public IntPtr VolumeIdentifier;
        public IntPtr Next;
        public IntPtr Prev;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MtpFile
    {
        public uint ItemId;
        public uint ParentId;
        public uint StorageId;
        public IntPtr FileName;
        public ulong FileSize;
        public long ModificationDate;
        public int FileType;
        public IntPtr Next;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProgressFunc(ulong sent, ulong total, IntPtr data);

    // ---------- 可用性 ----------

    internal static readonly bool IsAvailable = TryLoad();

    // ---------- 声明 ----------

    [DllImport("libmtp", EntryPoint = "LIBMTP_Init", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LIBMTP_Init();

    [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Connected_Devices", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int LIBMTP_Get_Connected_Devices(ref IntPtr list);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Release_Device_List", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LIBMTP_Release_Device_List(IntPtr list);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Serialnumber", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr LIBMTP_Get_Serialnumber(IntPtr device);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Friendlyname", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr LIBMTP_Get_Friendlyname(IntPtr device);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Modelname", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr LIBMTP_Get_Modelname(IntPtr device);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Storage", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int LIBMTP_Get_Storage(IntPtr device, int sortBy);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Files_And_Folders", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr LIBMTP_Get_Files_And_Folders(IntPtr device, uint storageId, uint parentId);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Get_File_To_File", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int LIBMTP_Get_File_To_File(
        IntPtr device, uint objectId, string path, ProgressFunc? progress, IntPtr data);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Send_File_From_File", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int LIBMTP_Send_File_From_File(
        IntPtr device, string path, IntPtr file, ProgressFunc? progress, IntPtr data);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Delete_Object", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int LIBMTP_Delete_Object(IntPtr device, uint objectId);

    [DllImport("libmtp", EntryPoint = "LIBMTP_new_file_t", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr LIBMTP_new_file_t();

    [DllImport("libmtp", EntryPoint = "LIBMTP_destroy_file_t", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void LIBMTP_destroy_file_t(IntPtr file);

    [DllImport("libmtp", EntryPoint = "LIBMTP_Set_File_Name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int LIBMTP_Set_File_Name(IntPtr device, IntPtr file, string name);

    [DllImport("libc.so.6", EntryPoint = "free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(IntPtr ptr);

    // ---------- 辅助 ----------

    internal static IntPtr DeviceNext(IntPtr device)
        => Marshal.PtrToStructure<MtpDevice>(device).Next;

    internal static IntPtr FileNext(IntPtr file)
        => Marshal.PtrToStructure<MtpFile>(file).Next;

    /// <summary>把 libmtp 返回的 char* 转为 string 并释放内存。</summary>
    internal static string? TakeString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var text = Marshal.PtrToStringUTF8(ptr);
            Free(ptr);
            return text;
        }
        catch { return null; }
    }

    /// <summary>获取设备稳定 Key：序列号优先，退化为友好名 / 型号。</summary>
    internal static string GetDeviceKey(IntPtr device)
    {
        var serial = TakeString(LIBMTP_Get_Serialnumber(device));
        if (!string.IsNullOrWhiteSpace(serial)) return serial;

        var friendly = TakeString(LIBMTP_Get_Friendlyname(device));
        var model = TakeString(LIBMTP_Get_Modelname(device));

        return !string.IsNullOrWhiteSpace(friendly)
            ? friendly
            : (!string.IsNullOrWhiteSpace(model) ? model : "unknown");
    }

    private static bool TryLoad()
    {
        foreach (var name in new[] { "libmtp.so.9", "libmtp.so.10", "libmtp.so", "libmtp" })
            if (NativeLibrary.TryLoad(name, out _)) return true;
        return false;
    }
}
