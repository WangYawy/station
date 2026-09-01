using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Station.Application.Collecting;
using Station.Application.Recorders;
using Station.Contracts;

namespace Station.Desktop.Infrastructure.Collecting;

/// <summary>
/// 真实 MTP 采集源（Linux）：基于 libmtp（Ubuntu/Kylin/UOS 包 libmtp9）P/Invoke。
/// 枚举 MTP 设备、递归扫描文件、下载到缓存、普通删除擦除；绑定文件读写删走 MTP 内容接口。
/// 未安装 libmtp 或未连接设备时给出明确错误；MTP 仅真实设备模式，无模拟回退。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMtpCollectSource : ICollectSource, IRecorderRootFileStore
{
    private const char PartSeparator = '\u001F';
    private const uint FilesAndFoldersRoot = 0xFFFFFFFF;

    private readonly CollectOptions _options;

    public LinuxMtpCollectSource(CollectOptions options)
    {
        _options = options;
    }

    public string SourceKey => "mtp-linux";

    // ==================== ICollectSource ====================

    public string GetRecorderRoot(CollectDeviceInfo device)
    {
        var devices = DetectDevices(_options);
        var first = devices.FirstOrDefault();
        return first is null
            ? throw new InvalidOperationException("未检测到 MTP 设备（请通过 USB 连接记录仪并选择 MTP 模式）")
            : first.Root;
    }

    public Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        CollectDeviceInfo device,
        CancellationToken cancellationToken)
    {
        var root = device.RootPath ?? GetRecorderRoot(device);
        using var session = OpenByRoot(root);
        var files = new List<SourceFileInfo>();
        Walk(session, FilesAndFoldersRoot, root, files, cancellationToken);
        return Task.FromResult<IReadOnlyList<SourceFileInfo>>(files);
    }

    public async Task CopyAsync(
        CollectDeviceInfo device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var root = device.RootPath ?? GetRecorderRoot(device);
        var (_, objectId) = ParseRelativePath(file.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var session = OpenByRoot(root);
        cancellationToken.ThrowIfCancellationRequested();
        var temp = destinationPath + ".mtp";
        try
        {
            var state = new ProgressState(onProgress, file.Size, cancellationToken);
            var gc = GCHandle.Alloc(state);
            try
            {
                var result = Native.LIBMTP_Get_File_To_File(
                    session.Device, objectId, temp, ProgressCallback, GCHandle.ToIntPtr(gc));
                if (result != 0)
                {
                    throw new IOException($"MTP 读取文件失败（libmtp 错误码 {result}）");
                }
            }
            finally
            {
                gc.Free();
            }

            if (File.Exists(temp))
            {
                File.Move(temp, destinationPath, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public Task EraseAsync(CollectDeviceInfo device, CancellationToken cancellationToken)
    {
        var root = device.RootPath ?? GetRecorderRoot(device);
        using var session = OpenByRoot(root);
        var targets = new List<uint>();
        CollectFiles(session, FilesAndFoldersRoot, targets, cancellationToken);
        foreach (var objectId in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Native.LIBMTP_Delete_Object(session.Device, objectId);
        }

        return Task.CompletedTask;
    }

    // ==================== IRecorderRootFileStore（绑定文件） ====================

    public string? ReadFile(string recorderRoot, string fileName)
    {
        using var session = OpenByRoot(recorderRoot);
        var node = FindRootFile(session, fileName);
        if (node == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var file = Marshal.PtrToStructure<Native.MtpFile>(node);
            var temp = Path.Combine(Path.GetTempPath(), "station-mtp-" + Guid.NewGuid().ToString("N"));
            try
            {
                var result = Native.LIBMTP_Get_File_To_File(session.Device, file.ItemId, temp, null, IntPtr.Zero);
                return result == 0 ? File.ReadAllText(temp) : null;
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
        finally
        {
            Native.LIBMTP_destroy_file_t(node);
        }
    }

    public void WriteFile(string recorderRoot, string fileName, string content)
    {
        using var session = OpenByRoot(recorderRoot);
        var existing = FindRootFile(session, fileName);
        if (existing != IntPtr.Zero)
        {
            var file = Marshal.PtrToStructure<Native.MtpFile>(existing);
            Native.LIBMTP_Delete_Object(session.Device, file.ItemId);
            Native.LIBMTP_destroy_file_t(existing);
        }

        var temp = Path.Combine(Path.GetTempPath(), "station-mtp-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(temp, content, Encoding.UTF8);
        try
        {
            var file = Native.LIBMTP_new_file_t();
            if (file == IntPtr.Zero)
            {
                throw new InvalidOperationException("MTP 创建文件对象失败");
            }

            try
            {
                var info = Marshal.PtrToStructure<Native.MtpFile>(file);
                info.ParentId = FilesAndFoldersRoot;
                info.StorageId = session.StorageId;
                info.FileSize = (ulong)new FileInfo(temp).Length;
                info.FileType = (int)Native.FileType.Text;
                Marshal.StructureToPtr(info, file, false);
                if (Native.LIBMTP_Set_File_Name(session.Device, file, fileName) != 0)
                {
                    throw new InvalidOperationException("MTP 设置文件名失败");
                }

                var result = Native.LIBMTP_Send_File_From_File(
                    session.Device, temp, file, null, IntPtr.Zero);
                if (result != 0)
                {
                    throw new InvalidOperationException($"MTP 写入绑定文件失败（libmtp 错误码 {result}）");
                }
            }
            finally
            {
                Native.LIBMTP_destroy_file_t(file);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public void DeleteFile(string recorderRoot, string fileName)
    {
        using var session = OpenByRoot(recorderRoot);
        var node = FindRootFile(session, fileName);
        if (node == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var file = Marshal.PtrToStructure<Native.MtpFile>(node);
            Native.LIBMTP_Delete_Object(session.Device, file.ItemId);
        }
        finally
        {
            Native.LIBMTP_destroy_file_t(node);
        }
    }

    // ==================== 设备检测 ====================

    public static IReadOnlyList<DetectedDevice> DetectDevices(CollectOptions options)
    {
        if (!OperatingSystem.IsLinux() || !Native.IsAvailable)
        {
            return [];
        }

        Native.LIBMTP_Init();
        IntPtr list = IntPtr.Zero;
        var error = Native.LIBMTP_Get_Connected_Devices(ref list);
        if (error != (int)Native.Error.None || list == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var result = new List<DetectedDevice>();
            for (var node = list; node != IntPtr.Zero; node = Native.DeviceNext(node))
            {
                var key = DeviceKey(node);
                var friendly = GetString(Native.LIBMTP_Get_Friendlyname(node)) ??
                               GetString(Native.LIBMTP_Get_Modelname(node)) ??
                               key;
                if (!string.IsNullOrWhiteSpace(options.MtpDeviceFilter) &&
                    !friendly.Contains(options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase) &&
                    !key.Contains(options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(new DetectedDevice(
                    "MTP:" + key,
                    friendly,
                    null,
                    ProtocolType.Mtp,
                    MtpRoot.RootScheme + key));
            }

            return result;
        }
        finally
        {
            Native.LIBMTP_Release_Device_List(list);
        }
    }

    private static string DeviceKey(IntPtr device)
    {
        var serial = GetString(Native.LIBMTP_Get_Serialnumber(device));
        if (!string.IsNullOrWhiteSpace(serial))
        {
            return serial;
        }

        var friendly = GetString(Native.LIBMTP_Get_Friendlyname(device));
        var model = GetString(Native.LIBMTP_Get_Modelname(device));
        return string.IsNullOrWhiteSpace(friendly)
            ? (string.IsNullOrWhiteSpace(model) ? "unknown" : model)
            : friendly;
    }

    private static string? GetString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var text = Marshal.PtrToStringUTF8(ptr);
            Native.Free(ptr);
            return text;
        }
        catch
        {
            return null;
        }
    }

    // ==================== 会话与遍历 ====================

    private MtpSession OpenByRoot(string recorderRoot)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("LinuxMtpCollectSource 仅支持 Linux");
        }

        if (!Native.IsAvailable)
        {
            throw new InvalidOperationException("未安装 libmtp（Linux 上请安装 libmtp9，如 sudo apt install libmtp9）");
        }

        var key = ParseRoot(recorderRoot);
        Native.LIBMTP_Init();
        IntPtr list = IntPtr.Zero;
        var error = Native.LIBMTP_Get_Connected_Devices(ref list);
        if (error != (int)Native.Error.None || list == IntPtr.Zero)
        {
            throw new InvalidOperationException("未检测到 MTP 设备（请通过 USB 连接记录仪并选择 MTP 模式）");
        }

        for (var node = list; node != IntPtr.Zero; node = Native.DeviceNext(node))
        {
            if (DeviceKey(node) == key)
            {
                Native.LIBMTP_Get_Storage(node, 0);
                return new MtpSession(list, node, FirstStorageId(node));
            }
        }

        Native.LIBMTP_Release_Device_List(list);
        throw new InvalidOperationException("MTP 设备已更换或未连接，与绑定根目录不一致，请重新接入记录仪");
    }

    private static uint FirstStorageId(IntPtr device)
    {
        var info = Marshal.PtrToStructure<Native.MtpDevice>(device);
        return info.Storage == IntPtr.Zero
            ? 0
            : Marshal.PtrToStructure<Native.MtpStorage>(info.Storage).Id;
    }

    private void Walk(
        MtpSession session,
        uint parentId,
        string root,
        List<SourceFileInfo> files,
        CancellationToken cancellationToken)
    {
        var list = Native.LIBMTP_Get_Files_And_Folders(session.Device, session.StorageId, parentId);
        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = Marshal.PtrToStructure<Native.MtpFile>(node);
                if (file.FileType == (int)Native.FileType.Folder)
                {
                    Walk(session, file.ItemId, root, files, cancellationToken);
                    continue;
                }

                var name = Marshal.PtrToStringUTF8(file.FileName) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var key = root[MtpRoot.RootScheme.Length..];
                files.Add(new SourceFileInfo(
                    $"{key}{PartSeparator}{file.ItemId}",
                    name,
                    (long)file.FileSize,
                    DateTimeOffset.FromUnixTimeSeconds(file.ModificationDate).UtcDateTime));
            }
        }
        finally
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                nodes.Add(node);
            }

            foreach (var node in nodes)
            {
                Native.LIBMTP_destroy_file_t(node);
            }
        }
    }

    private void CollectFiles(MtpSession session, uint parentId, List<uint> targets, CancellationToken cancellationToken)
    {
        var list = Native.LIBMTP_Get_Files_And_Folders(session.Device, session.StorageId, parentId);
        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = Marshal.PtrToStructure<Native.MtpFile>(node);
                if (file.FileType == (int)Native.FileType.Folder)
                {
                    CollectFiles(session, file.ItemId, targets, cancellationToken);
                    continue;
                }

                var name = Marshal.PtrToStringUTF8(file.FileName) ?? string.Empty;
                if (_options.FileExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
                {
                    targets.Add(file.ItemId);
                }
            }
        }
        finally
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                nodes.Add(node);
            }

            foreach (var node in nodes)
            {
                Native.LIBMTP_destroy_file_t(node);
            }
        }
    }

    private static IntPtr FindRootFile(MtpSession session, string fileName)
    {
        var list = Native.LIBMTP_Get_Files_And_Folders(session.Device, session.StorageId, FilesAndFoldersRoot);
        IntPtr match = IntPtr.Zero;
        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                var file = Marshal.PtrToStructure<Native.MtpFile>(node);
                if (file.FileType == (int)Native.FileType.Folder)
                {
                    continue;
                }

                var name = Marshal.PtrToStringUTF8(file.FileName) ?? string.Empty;
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    match = node;
                    break;
                }
            }
        }
        finally
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                nodes.Add(node);
            }

            foreach (var node in nodes)
            {
                if (node != match)
                {
                    Native.LIBMTP_destroy_file_t(node);
                }
            }
        }

        return match;
    }

    private static (string Key, uint ObjectId) ParseRelativePath(string relativePath)
    {
        var index = relativePath.IndexOf(PartSeparator);
        if (index < 0 || !uint.TryParse(relativePath[(index + 1)..], out var objectId))
        {
            throw new InvalidOperationException($"无效的 MTP 文件路径: {relativePath}");
        }

        return (relativePath[..index], objectId);
    }

    private static string ParseRoot(string recorderRoot)
    {
        if (!recorderRoot.StartsWith(MtpRoot.RootScheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"不是 MTP 虚拟根目录: {recorderRoot}");
        }

        return recorderRoot[MtpRoot.RootScheme.Length..];
    }

    private static int ProgressCallback(ulong sent, ulong total, IntPtr data)
    {
        if (data == IntPtr.Zero)
        {
            return 0;
        }

        var state = (ProgressState?)GCHandle.FromIntPtr(data).Target;
        if (state is null)
        {
            return 0;
        }

        if (state.Token.IsCancellationRequested)
        {
            return 1;
        }

        if (state.OnProgress is not null)
        {
            state.OnProgress(total <= 0 ? 1d : Math.Min(1d, (double)sent / total)).GetAwaiter().GetResult();
        }

        return 0;
    }

    private sealed record ProgressState(Func<double, Task>? OnProgress, long Total, CancellationToken Token);

    private sealed class MtpSession : IDisposable
    {
        public MtpSession(IntPtr list, IntPtr device, uint storageId)
        {
            List = list;
            Device = device;
            StorageId = storageId;
        }

        public IntPtr List { get; }
        public IntPtr Device { get; }
        public uint StorageId { get; }

        public void Dispose()
        {
            if (List != IntPtr.Zero)
            {
                Native.LIBMTP_Release_Device_List(List);
            }
        }
    }

    // ==================== libmtp P/Invoke ====================

    private static class Native
    {
        public enum Error
        {
            None = 0,
            General = 1,
            NoDeviceAttached = 2,
            Connecting = 3
        }

        public enum FileType
        {
            Folder = 0,
            Text = 28,
            Unknown = 46
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MtpDevice
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
        public struct MtpStorage
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
        public struct MtpFile
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
        public delegate int ProgressFunc(ulong sent, ulong total, IntPtr data);

        public static readonly bool IsAvailable = TryLoad();

        [DllImport("libmtp", EntryPoint = "LIBMTP_Init", CallingConvention = CallingConvention.Cdecl)]
        public static extern void LIBMTP_Init();

        [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Connected_Devices", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LIBMTP_Get_Connected_Devices(ref IntPtr list);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Release_Device_List", CallingConvention = CallingConvention.Cdecl)]
        public static extern void LIBMTP_Release_Device_List(IntPtr list);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Release_Device", CallingConvention = CallingConvention.Cdecl)]
        public static extern void LIBMTP_Release_Device(IntPtr device);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Serialnumber", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr LIBMTP_Get_Serialnumber(IntPtr device);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Friendlyname", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr LIBMTP_Get_Friendlyname(IntPtr device);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Modelname", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr LIBMTP_Get_Modelname(IntPtr device);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Storage", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LIBMTP_Get_Storage(IntPtr device, int sortBy);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Get_Files_And_Folders", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr LIBMTP_Get_Files_And_Folders(IntPtr device, uint storageId, uint parentId);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Get_File_To_File", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LIBMTP_Get_File_To_File(
            IntPtr device, uint objectId, string path, ProgressFunc? progress, IntPtr data);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Send_File_From_File", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LIBMTP_Send_File_From_File(
            IntPtr device, string path, IntPtr file, ProgressFunc? progress, IntPtr data);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Delete_Object", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LIBMTP_Delete_Object(IntPtr device, uint objectId);

        [DllImport("libmtp", EntryPoint = "LIBMTP_new_file_t", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr LIBMTP_new_file_t();

        [DllImport("libmtp", EntryPoint = "LIBMTP_destroy_file_t", CallingConvention = CallingConvention.Cdecl)]
        public static extern void LIBMTP_destroy_file_t(IntPtr file);

        [DllImport("libmtp", EntryPoint = "LIBMTP_Set_File_Name", CallingConvention = CallingConvention.Cdecl)]
        public static extern int LIBMTP_Set_File_Name(IntPtr device, IntPtr file, string name);

        [DllImport("libc.so.6", EntryPoint = "free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Free(IntPtr ptr);

        public static IntPtr DeviceNext(IntPtr device) =>
            Marshal.PtrToStructure<MtpDevice>(device).Next;

        public static IntPtr FileNext(IntPtr file) =>
            Marshal.PtrToStructure<MtpFile>(file).Next;

        private static bool TryLoad()
        {
            foreach (var name in new[] { "libmtp.so.9", "libmtp.so.10", "libmtp.so", "libmtp" })
            {
                if (NativeLibrary.TryLoad(name, out _))
                {
                    NativeLibrary.SetDllImportResolver(
                        typeof(LinuxMtpCollectSource).Assembly,
                        (libraryName, assembly, searchPath) =>
                        {
                            if (libraryName != "libmtp")
                            {
                                return IntPtr.Zero;
                            }

                            foreach (var candidate in new[] { "libmtp.so.9", "libmtp.so.10", "libmtp.so", "libmtp" })
                            {
                                if (NativeLibrary.TryLoad(candidate, out var handle))
                                {
                                    return handle;
                                }
                            }

                            return IntPtr.Zero;
                        });
                    return true;
                }
            }

            return false;
        }
    }
}
