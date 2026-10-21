using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.FileSystem;
using Station.Device.Runtime.Interop;

namespace Station.Device.Runtime.FileSystem;

/// <summary>
/// Linux / Kylin / UOS MTP 文件系统实现（libmtp）。
/// 所有阻塞调用包装在 <see cref="Task.Run{TResult}(Func{TResult}, CancellationToken)"/> 中，避免阻塞调用线程。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMtpFileSystem : IDeviceFileSystem
{
    private const int ProgressThrottleMs = 100;

    private readonly ILogger<LinuxMtpFileSystem> _logger;

    private static readonly ConcurrentDictionary<string, string> _objectIdCache = new();
    private static readonly SemaphoreSlim _libmtpGate = new(1, 1);

    public LinuxMtpFileSystem(ILogger<LinuxMtpFileSystem> logger)
    {
        _logger = logger;
    }

    public ProtocolType Protocol => ProtocolType.Mtp;

    public string GetRecorderRoot(DetectedDevice device) => device.Root;

    public Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        DetectedDevice device,
        IReadOnlyCollection<string>? extensions = null,
        IReadOnlyCollection<string>? includePathPrefixes = null,
        IReadOnlyCollection<string>? excludePathPrefixes = null,
        CancellationToken ct = default)
        => Task.Run<IReadOnlyList<SourceFileInfo>>(
         () => ScanCore(device, extensions, includePathPrefixes, excludePathPrefixes, ct), ct);

    public Task CopyAsync(
        DetectedDevice device, SourceFileInfo file, string destinationPath,
        Func<double, Task>? onProgress, CancellationToken ct)
        => Task.Run(() => CopyCore(device, file, destinationPath, onProgress, ct), ct);

    public Task<int> EraseAsync(
        DetectedDevice device, IEnumerable<SourceFileInfo> files, CancellationToken ct)
        => Task.Run(() => EraseCore(device, files, ct), ct);

    public bool FileExists(DetectedDevice device, string relativePath)
    {
        try
        {
            using var session = OpenSession(device.Root);
            return FindRootFile(session, relativePath) != IntPtr.Zero;
        }
        catch { return false; }
    }

    public string? ReadAllText(DetectedDevice device, string relativePath)
    {
        try
        {
            using var session = OpenSession(device.Root);
            var node = FindRootFile(session, relativePath);
            if (node == IntPtr.Zero) return null;

            try
            {
                var file = Marshal.PtrToStructure<LibMtpInterop.MtpFile>(node);
                var temp = Path.Combine(Path.GetTempPath(), "station-mtp-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var result = LibMtpInterop.LIBMTP_Get_File_To_File(
                        session.Device, file.ItemId, temp, null, IntPtr.Zero);
                    return result == 0 ? File.ReadAllText(temp) : null;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            finally { LibMtpInterop.LIBMTP_destroy_file_t(node); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MTP 读文件失败：{File}", relativePath);
            return null;
        }
    }

    public void WriteAllText(DetectedDevice device, string relativePath, string content)
        => throw new NotSupportedException("Linux MTP 暂不支持写入文件");

    public void DeleteFile(DetectedDevice device, string relativePath)
    {
        try
        {
            using var session = OpenSession(device.Root);
            var node = FindRootFile(session, relativePath);
            if (node == IntPtr.Zero) return;
            try
            {
                var file = Marshal.PtrToStructure<LibMtpInterop.MtpFile>(node);
                LibMtpInterop.LIBMTP_Delete_Object(session.Device, file.ItemId);
            }
            finally { LibMtpInterop.LIBMTP_destroy_file_t(node); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MTP 删除文件失败：{File}", relativePath);
        }
    }

    public (long? AvailableBytes, long? TotalBytes) GetDeviceSpace(DetectedDevice device)
    {
        if (!OperatingSystem.IsLinux()) return (null, null);

        try
        {
            using var session = OpenSession(device.Root);

            // session 构造时已调用 LIBMTP_Get_Storage(node, 0)，
            // storage 链表已填充在 MtpDevice.storage 上
            var deviceInfo = Marshal.PtrToStructure<LibMtpInterop.MtpDevice>(session.Device);

            long total = 0, free = 0;
            for (var s = deviceInfo.Storage; s != IntPtr.Zero; s = NextStorage(s))
            {
                try
                {
                    var storage = Marshal.PtrToStructure<LibMtpInterop.MtpStorage>(s);
                    total += (long)storage.MaxCapacity;
                    free += (long)storage.FreeSpaceInBytes;
                }
                catch
                {
                    // 单个存储不可读，跳过
                }
            }

            if (total <= 0) return (null, null);
            return (free, total);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Linux MTP 读取设备空间失败：{Root}", device.Root);
            return (null, null);
        }
    }

    private static IntPtr NextStorage(IntPtr current)
    {
        var storage = Marshal.PtrToStructure<LibMtpInterop.MtpStorage>(current);
        return storage.Next;
    }

    // ---------- 核心实现 ----------

    private IReadOnlyList<SourceFileInfo> ScanCore(
        DetectedDevice device,
        IReadOnlyCollection<string>? extensions,
        IReadOnlyCollection<string>? includePathPrefixes,
        IReadOnlyCollection<string>? excludePathPrefixes,
        CancellationToken ct)
    {
        using var session = OpenSession(device.Root);
        ClearCacheForDevice(session.DeviceKey);

        var files = new List<SourceFileInfo>();
        var stack = new Stack<string>();
        if (!string.IsNullOrWhiteSpace(session.StorageName)) stack.Push(session.StorageName);

        Walk(session, LibMtpInterop.FilesAndFoldersRoot, stack, files, ct,
            extensions, includePathPrefixes, excludePathPrefixes);

        if (stack.Count > 0) stack.Pop();
        return files;
    }

    private void CopyCore(
        DetectedDevice device, SourceFileInfo file, string destinationPath,
        Func<double, Task>? onProgress, CancellationToken ct)
    {
        using var session = OpenSession(device.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var objectId = ResolveObjectId(session, file);
        var temp = destinationPath + ".mtp";
        try
        {
            var state = new ProgressState(onProgress, ct);
            var gc = GCHandle.Alloc(state);
            try
            {
                var result = LibMtpInterop.LIBMTP_Get_File_To_File(
                    session.Device, objectId, temp, ProgressCallback, GCHandle.ToIntPtr(gc));
                if (result != 0)
                    throw new IOException($"MTP 读取文件失败（libmtp 错误码 {result}）");
            }
            finally { gc.Free(); }

            if (File.Exists(temp)) File.Move(temp, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        if (onProgress is not null)
            onProgress(1.0).GetAwaiter().GetResult();
    }

    private int EraseCore(DetectedDevice device, IEnumerable<SourceFileInfo> files, CancellationToken ct)
    {
        var list = files.ToList();
        if (list.Count == 0) return 0;

        using var session = OpenSession(device.Root);
        int deleted = 0;
        foreach (var file in list)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var objectId = ResolveObjectId(session, file);
                if (LibMtpInterop.LIBMTP_Delete_Object(session.Device, objectId) == 0)
                {
                    _objectIdCache.TryRemove(GetCacheKey(session.DeviceKey, file.RelativePath), out _);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MTP 擦除失败：{File}", file.RelativePath);
            }
        }
        return deleted;
    }


    // ---------- 内部工具 ----------

    private MtpSession OpenSession(string recorderRoot)
    {
        _libmtpGate.Wait();
        try
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("LinuxMtpFileSystem 仅支持 Linux");
            if (!LibMtpInterop.IsAvailable)
                throw new InvalidOperationException("未安装 libmtp（Linux 上请安装 libmtp9）");

            var key = ParseRoot(recorderRoot);
            LibMtpInterop.LIBMTP_Init();
            IntPtr list = IntPtr.Zero;
            var err = LibMtpInterop.LIBMTP_Get_Connected_Devices(ref list);
            if (err != 0 || list == IntPtr.Zero)
                throw new InvalidOperationException("未检测到 MTP 设备");

            for (var node = list; node != IntPtr.Zero; node = LibMtpInterop.DeviceNext(node))
            {
                if (LibMtpInterop.GetDeviceKey(node) == key)
                {
                    LibMtpInterop.LIBMTP_Get_Storage(node, 0);
                    var storageId = FirstStorageId(node);
                    var storageName = FirstStorageName(node);
                    return new MtpSession(list, node, storageId, storageName, key);
                }
            }

            LibMtpInterop.LIBMTP_Release_Device_List(list);
            throw new InvalidOperationException("MTP 设备已更换或未连接");
        }
        finally { _libmtpGate.Release(); }
    }

    private static uint FirstStorageId(IntPtr device)
    {
        var info = Marshal.PtrToStructure<LibMtpInterop.MtpDevice>(device);
        return info.Storage == IntPtr.Zero
            ? 0
            : Marshal.PtrToStructure<LibMtpInterop.MtpStorage>(info.Storage).Id;
    }

    private static string FirstStorageName(IntPtr device)
    {
        var info = Marshal.PtrToStructure<LibMtpInterop.MtpDevice>(device);
        if (info.Storage == IntPtr.Zero) return "Storage";

        var storage = Marshal.PtrToStructure<LibMtpInterop.MtpStorage>(info.Storage);
        var name = storage.StorageDescription != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(storage.StorageDescription)
            : null;
        return string.IsNullOrWhiteSpace(name) ? "Storage" : name;
    }

    private void Walk(
        MtpSession session, uint parentId, Stack<string> pathStack,
        List<SourceFileInfo> files, CancellationToken ct,
        IReadOnlyCollection<string>? extensions,
        IReadOnlyCollection<string>? includePathPrefixes,
        IReadOnlyCollection<string>? excludePathPrefixes)
    {
        var list = LibMtpInterop.LIBMTP_Get_Files_And_Folders(
            session.Device, session.StorageId, parentId);

        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = LibMtpInterop.FileNext(node))
            {
                ct.ThrowIfCancellationRequested();
                nodes.Add(node);

                var file = Marshal.PtrToStructure<LibMtpInterop.MtpFile>(node);
                var name = Marshal.PtrToStringUTF8(file.FileName) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (file.FileType == (int)LibMtpInterop.FileType.Folder)
                {
                    pathStack.Push(name);

                    // 目录剪枝：当前完整相对路径（含 storage 名）是否值得下钻
                    var childRel = string.Join("/", pathStack.Reverse());
                    if (ScanFilter.ShouldDescend(childRel, includePathPrefixes))
                    {
                        Walk(session, file.ItemId, pathStack, files, ct,
                            extensions, includePathPrefixes, excludePathPrefixes);
                    }

                    pathStack.Pop();
                    continue;
                }

                var relativePath = BuildPath(pathStack, name);

                if (!ScanFilter.IsPathAllowed(relativePath, includePathPrefixes, excludePathPrefixes)) continue;
                if (!ScanFilter.IsExtensionAllowed(name, extensions)) continue;

                files.Add(new SourceFileInfo(
                    RelativePath: relativePath,
                    FileName: name,
                    Size: (long)file.FileSize,
                    ModifiedAt: DateTimeOffset.FromUnixTimeSeconds(file.ModificationDate).UtcDateTime,
                    ObjectPath: $"{session.DeviceKey}{LibMtpInterop.PartSeparator}{file.ItemId}"));

                _objectIdCache[GetCacheKey(session.DeviceKey, relativePath)] = file.ItemId.ToString();
            }
        }
        finally
        {
            foreach (var node in nodes) LibMtpInterop.LIBMTP_destroy_file_t(node);
        }
    }

    private static string BuildPath(Stack<string> stack, string fileName)
    {
        if (stack.Count == 0) return fileName;
        return string.Join(LibMtpInterop.PathSeparator, stack.Reverse()) + LibMtpInterop.PathSeparator + fileName;
    }

    private static uint ResolveObjectId(MtpSession session, SourceFileInfo file)
    {
        if (!string.IsNullOrEmpty(file.ObjectPath))
        {
            var idx = file.ObjectPath.IndexOf(LibMtpInterop.PartSeparator);
            if (idx > 0 && uint.TryParse(file.ObjectPath[(idx + 1)..], out var id))
                return id;
        }

        var cacheKey = GetCacheKey(session.DeviceKey, file.RelativePath);
        if (_objectIdCache.TryGetValue(cacheKey, out var cached)
            && uint.TryParse(cached, out var cachedId))
            return cachedId;

        throw new FileNotFoundException($"MTP 设备上未找到文件：{file.RelativePath}");
    }

    private static IntPtr FindRootFile(MtpSession session, string fileName)
    {
        var list = LibMtpInterop.LIBMTP_Get_Files_And_Folders(
            session.Device, session.StorageId, LibMtpInterop.FilesAndFoldersRoot);
        if (list == IntPtr.Zero) return IntPtr.Zero;

        IntPtr match = IntPtr.Zero;
        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = LibMtpInterop.FileNext(node))
            {
                nodes.Add(node);
                var file = Marshal.PtrToStructure<LibMtpInterop.MtpFile>(node);
                if (file.FileType == (int)LibMtpInterop.FileType.Folder) continue;

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
            foreach (var n in nodes) if (n != match) LibMtpInterop.LIBMTP_destroy_file_t(n);
        }
        return match;
    }

    private static string GetCacheKey(string deviceKey, string relativePath)
        => $"{deviceKey}|{relativePath}";

    private static void ClearCacheForDevice(string deviceKey)
    {
        var prefix = $"{deviceKey}|";
        foreach (var k in _objectIdCache.Keys)
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                _objectIdCache.TryRemove(k, out _);
    }

    private static string ParseRoot(string recorderRoot)
    {
        if (!recorderRoot.StartsWith(LibMtpInterop.RootScheme, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"不是 MTP 虚拟根目录: {recorderRoot}");
        return recorderRoot[LibMtpInterop.RootScheme.Length..];
    }

    private static int ProgressCallback(ulong sent, ulong total, IntPtr data)
    {
        if (data == IntPtr.Zero) return 0;
        var state = (ProgressState?)GCHandle.FromIntPtr(data).Target;
        if (state is null) return 0;
        if (state.Token.IsCancellationRequested) return 1;

        if (state.OnProgress is not null)
        {
            var now = Environment.TickCount64;
            var isFinal = total > 0 && sent >= total;
            if (isFinal || now - state.LastReportMs >= ProgressThrottleMs)
            {
                state.LastReportMs = now;
                state.OnProgress(total == 0 ? 1d : Math.Min(1d, (double)sent / total))
                    .GetAwaiter().GetResult();
            }
        }
        return 0;
    }

    private sealed class ProgressState
    {
        public ProgressState(Func<double, Task>? onProgress, CancellationToken token)
        {
            OnProgress = onProgress;
            Token = token;
        }
        public Func<double, Task>? OnProgress { get; }
        public CancellationToken Token { get; }
        public long LastReportMs;
    }

    private sealed class MtpSession : IDisposable
    {
        public MtpSession(IntPtr list, IntPtr device, uint storageId, string storageName, string deviceKey)
        {
            List = list; Device = device; StorageId = storageId;
            StorageName = storageName; DeviceKey = deviceKey;
        }
        public IntPtr List { get; }
        public IntPtr Device { get; }
        public uint StorageId { get; }
        public string StorageName { get; }
        public string DeviceKey { get; }

        public void Dispose()
        {
            if (List != IntPtr.Zero) LibMtpInterop.LIBMTP_Release_Device_List(List);
        }
    }
}
