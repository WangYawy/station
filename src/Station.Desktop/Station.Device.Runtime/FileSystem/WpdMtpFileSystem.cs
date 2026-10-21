using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.FileSystem;
using Station.Device.Runtime.Interop;
using Vanara.PInvoke;

namespace Station.Device.Runtime.FileSystem;

/// <summary>
/// Windows MTP 文件系统实现（WPD）。
/// 阻塞 COM 调用包装在 <see cref="Task.Run(Action, CancellationToken)"/> 中。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WpdMtpFileSystem : IDeviceFileSystem
{
    private const int StreamBufferSize = 64 * 1024;
    private const int ProgressThrottleMs = 100;

    private readonly ILogger<WpdMtpFileSystem> _logger;

    public WpdMtpFileSystem(ILogger<WpdMtpFileSystem> logger)
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
            var pnpId = ExtractPnpId(device.Root);
            using var session = OpenDevice(pnpId, readOnly: true);
            return FindFileRecursive(session, relativePath, PortableDeviceApi.WPD_DEVICE_OBJECT_ID) != null;
        }
        catch { return false; }
    }

    public string? ReadAllText(DetectedDevice device, string relativePath)
    {
        try
        {
            var pnpId = ExtractPnpId(device.Root);
            using var session = OpenDevice(pnpId, readOnly: true);
            var id = FindFileRecursive(session, relativePath, PortableDeviceApi.WPD_DEVICE_OBJECT_ID);
            return id is null ? null : ReadObjectContent(session, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WPD 读文件失败：{File}", relativePath);
            return null;
        }
    }

    public void WriteAllText(DetectedDevice device, string relativePath, string content)
    {
        var pnpId = ExtractPnpId(device.Root);
        using var session = OpenDevice(pnpId, readOnly: false);

        var parent = ResolveWriteParent(session);
        var values = (PortableDeviceApi.IPortableDeviceValues)new PortableDeviceApi.PortableDeviceValues();
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_PARENT_ID, parent);
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_NAME, Path.GetFileName(relativePath));
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME, Path.GetFileName(relativePath));
        values.SetGuidValue(PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE, PortableDeviceApi.WPD_CONTENT_TYPE_DOCUMENT);
        values.SetGuidValue(PortableDeviceApi.WPD_OBJECT_FORMAT, PortableDeviceApi.WPD_OBJECT_FORMAT_TEXT);

        var bytes = Encoding.UTF8.GetBytes(content);
        try
        {
            uint optimal = 0;
            var created = session.Content.CreateObjectWithPropertiesAndData(values, out var stream, ref optimal);
            try
            {
                var written = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    stream.Write(bytes, bytes.Length, written);
                    stream.Commit(0);
                }
                finally { Marshal.FreeHGlobal(written); }
            }
            finally { Marshal.ReleaseComObject(stream); }
            _ = created;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"MTP 设备不支持写入 {relativePath}：{ex.Message}", ex);
        }
    }

    public void DeleteFile(DetectedDevice device, string relativePath)
    {
        var pnpId = ExtractPnpId(device.Root);
        using var session = OpenDevice(pnpId, readOnly: false);
        var id = FindFileRecursive(session, relativePath, PortableDeviceApi.WPD_DEVICE_OBJECT_ID);
        if (id is not null) DeleteObject(session.Content, id);
    }


    public (long? AvailableBytes, long? TotalBytes) GetDeviceSpace(DetectedDevice device)
    {
        if (!OperatingSystem.IsWindows()) return (null, null);

        try
        {
            var pnpId = ExtractPnpId(device.Root);
            using var session = OpenDevice(pnpId, readOnly: true);

            long total = 0, free = 0;
            foreach (var storageId in session.StorageIds)
            {
                try
                {
                    var values = session.Properties.GetValues(storageId, session.SpaceKeys);

                    var capacity = values.GetUnsignedLargeIntegerValue(
                        PortableDeviceApi.WPD_STORAGE_CAPACITY);
                    var freeBytes = values.GetUnsignedLargeIntegerValue(
                        PortableDeviceApi.WPD_STORAGE_FREE_SPACE_IN_BYTES);

                    total += (long)capacity;
                    free += (long)freeBytes;
                }
                catch
                {
                    // 单个存储对象不可读，跳过
                }
            }

            if (total <= 0) return (null, null);
            return (free, total);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WPD MTP 读取设备空间失败：{Root}", device.Root);
            return (null, null);
        }
    }

    // ---------- 核心 ----------

    private IReadOnlyList<SourceFileInfo> ScanCore(
    DetectedDevice device,
    IReadOnlyCollection<string>? extensions,
    IReadOnlyCollection<string>? includePathPrefixes,
    IReadOnlyCollection<string>? excludePathPrefixes,
    CancellationToken ct)
    {
        var pnpId = ExtractPnpId(device.Root);
        using var session = OpenDevice(pnpId, readOnly: true);

        var list = new List<SourceFileInfo>();

        if (session.StorageIds.Count > 0)
        {
            foreach (var storageId in session.StorageIds)
            {
                WalkObjects(session, storageId, pnpId, list, ct,
                    extensions, includePathPrefixes, excludePathPrefixes);
            }
        }
        else
        {
            WalkObjects(session, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, pnpId, list, ct,
                extensions, includePathPrefixes, excludePathPrefixes);
        }

        return list;
    }

    private void CopyCore(
        DetectedDevice device, SourceFileInfo file, string destinationPath,
        Func<double, Task>? onProgress, CancellationToken ct)
    {
        var pnpId = ExtractPnpId(device.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var session = OpenDevice(pnpId, readOnly: true);
        var objectId = ResolveObjectId(session, file);

        var stream = session.Resources.GetStream(
            objectId, PortableDeviceApi.WPD_RESOURCE_DEFAULT, STGM.STGM_READ, out _);
        try
        {
            using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, StreamBufferSize);
            CopyStreamSync(stream, output, file.Size, onProgress, ct);
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    private int EraseCore(DetectedDevice device, IEnumerable<SourceFileInfo> files, CancellationToken ct)
    {
        var list = files.ToList();
        if (list.Count == 0) return 0;

        var pnpId = ExtractPnpId(device.Root);
        using var session = OpenDevice(pnpId, readOnly: false);
        int deleted = 0;
        foreach (var file in list)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var id = ResolveObjectId(session, file);
                DeleteObject(session.Content, id);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WPD 擦除失败：{File}", file.RelativePath);
            }
        }
        return deleted;
    }

    // ---------- 内部 ----------

    private void WalkObjects(
        WpdSession session, string parentId, string pnpId,
        List<SourceFileInfo> files, CancellationToken ct,
        IReadOnlyCollection<string>? extensions,
        IReadOnlyCollection<string>? includePathPrefixes,
        IReadOnlyCollection<string>? excludePathPrefixes,
        Stack<string>? stack = null)
    {
        var isTopStorage = stack is null && session.IsStorageId(parentId);
        stack ??= new Stack<string>();
        if (isTopStorage)
        {
            var name = session.GetStorageName(parentId);
            if (!string.IsNullOrWhiteSpace(name)) stack.Push(name);
        }

        try
        {
            foreach (var objectId in EnumerateChildren(session.Content, parentId, ct))
            {
                ct.ThrowIfCancellationRequested();

                var values = session.Properties.GetValues(objectId, session.ScanKeys);

                if (IsStorage(values))
                {
                    // 存储层不剪枝：其子容器再往下才做剪枝
                    var sn = GetStorageName(values);
                    var pushed = !string.IsNullOrWhiteSpace(sn);
                    if (pushed) stack.Push(sn!);

                    WalkObjects(session, objectId, pnpId, files, ct,
                        extensions, includePathPrefixes, excludePathPrefixes, stack);

                    if (pushed) stack.Pop();
                    continue;
                }

                if (IsContainer(values))
                {
                    var fn = GetObjectName(values);
                    var pushed = !string.IsNullOrWhiteSpace(fn);
                    if (pushed) stack.Push(fn!);

                    var currentRel = string.Join("/", stack.Reverse());
                    if (ScanFilter.ShouldDescend(currentRel, includePathPrefixes))
                    {
                        WalkObjects(session, objectId, pnpId, files, ct,
                            extensions, includePathPrefixes, excludePathPrefixes, stack);
                    }

                    if (pushed) stack.Pop();
                    continue;
                }

                // 文件
                var name = GetObjectName(values);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var relativePath = BuildPath(stack, name);

                if (!ScanFilter.IsPathAllowed(relativePath, includePathPrefixes, excludePathPrefixes)) continue;
                if (!ScanFilter.IsExtensionAllowed(name, extensions)) continue;

                var size = (long)SafeGetUInt64(values, PortableDeviceApi.PKEY_GenericObj_ObjectSize);
                var modified = SafeGetDate(values) ?? DateTime.UtcNow;

                files.Add(new SourceFileInfo(
                    RelativePath: relativePath,
                    FileName: name,
                    Size: size,
                    ModifiedAt: modified,
                    ObjectPath: $"{pnpId}{WpdInterop.PartSeparator}{objectId}"));
            }
        }
        finally
        {
            if (isTopStorage && stack.Count > 0) stack.Pop();
        }
    }

    private static string BuildPath(Stack<string> stack, string name)
        => stack.Count == 0
            ? name
            : string.Join(WpdInterop.PathSeparator, stack.Reverse()) + WpdInterop.PathSeparator + name;

    private static string? ResolveObjectId(WpdSession session, SourceFileInfo file)
    {
        if (!string.IsNullOrEmpty(file.ObjectPath))
        {
            var idx = file.ObjectPath.IndexOf(WpdInterop.PartSeparator);
            if (idx > 0) return file.ObjectPath[(idx + 1)..];
        }
        throw new FileNotFoundException($"WPD 未找到文件：{file.RelativePath}");
    }

    private static string? FindFileRecursive(WpdSession session, string fileName, string parentId)
    {
        foreach (var objectId in EnumerateChildren(session.Content, parentId, CancellationToken.None))
        {
            var values = session.Properties.GetValues(objectId, session.ScanKeys);
            if (IsStorage(values) || IsContainer(values))
            {
                var r = FindFileRecursive(session, fileName, objectId);
                if (r is not null) return r;
                continue;
            }

            var name = GetObjectName(values);
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return objectId;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateChildren(
        PortableDeviceApi.IPortableDeviceContent content, string parentId, CancellationToken ct)
    {
        var e = content.EnumObjects(0, parentId, null);
        var buffer = new string[256];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var hr = e.Next((uint)buffer.Length, buffer, out var fetched);
            hr.ThrowIfFailed("WPD 枚举失败");
            if (fetched == 0) yield break;
            for (var i = 0; i < fetched; i++) yield return buffer[i];
            if (fetched < buffer.Length) yield break;
        }
    }

    private static bool IsStorage(PortableDeviceApi.IPortableDeviceValues v)
    {
        try { _ = v.GetUnsignedLargeIntegerValue(PortableDeviceApi.WPD_STORAGE_CAPACITY); return true; }
        catch { return false; }
    }

    private static bool IsContainer(PortableDeviceApi.IPortableDeviceValues v)
    {
        try
        {
            var t = v.GetGuidValue(PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE);
            return t == PortableDeviceApi.WPD_CONTENT_TYPE_FOLDER
                || t == PortableDeviceApi.WPD_CONTENT_TYPE_FUNCTIONAL_OBJECT;
        }
        catch { return false; }
    }

    private static string GetObjectName(PortableDeviceApi.IPortableDeviceValues v)
        => SafeGetString(v, PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME)
           ?? SafeGetString(v, PortableDeviceApi.WPD_OBJECT_NAME)
           ?? string.Empty;

    private static string? GetStorageName(PortableDeviceApi.IPortableDeviceValues v)
        => SafeGetString(v, PortableDeviceApi.WPD_OBJECT_NAME);

    private static string? SafeGetString(PortableDeviceApi.IPortableDeviceValues v, Ole32.PROPERTYKEY k)
    {
        try { return v.GetStringValue(k); } catch { return null; }
    }

    private static ulong SafeGetUInt64(PortableDeviceApi.IPortableDeviceValues v, Ole32.PROPERTYKEY k)
    {
        try { return v.GetUnsignedLargeIntegerValue(k); } catch { return 0; }
    }

    private static DateTime? SafeGetDate(PortableDeviceApi.IPortableDeviceValues v)
    {
        try
        {
            var value = v.GetValue(PortableDeviceApi.PKEY_GenericObj_DateModified).Value;
            return value switch
            {
                DateTime d => d.ToUniversalTime(),
                long ticks => new DateTime(ticks, DateTimeKind.Utc),
                _ => null
            };
        }
        catch { return null; }
    }

    private static void DeleteObject(PortableDeviceApi.IPortableDeviceContent content, string objectId)
    {
        var ids = (PortableDeviceApi.IPortableDevicePropVariantCollection)
            new PortableDeviceApi.PortableDevicePropVariantCollection();
        var value = new Ole32.PROPVARIANT_UNMGD(objectId, VarEnum.VT_LPWSTR);
        ids.Add(in value);
        content.Delete(PortableDeviceApi.DELETE_OBJECT_OPTIONS.PORTABLE_DEVICE_DELETE_WITH_RECURSION, ids);
    }

    private static string ResolveWriteParent(WpdSession session)
    {
        foreach (var storageId in session.StorageIds)
        {
            foreach (var childId in EnumerateChildren(session.Content, storageId, CancellationToken.None))
            {
                var values = session.Properties.GetValues(childId, session.ScanKeys);
                if (IsContainer(values)) return childId;
            }
            return storageId;
        }
        return PortableDeviceApi.WPD_DEVICE_OBJECT_ID;
    }

    private string? ReadObjectContent(WpdSession session, string objectId)
    {
        var stream = session.Resources.GetStream(
            objectId, PortableDeviceApi.WPD_RESOURCE_DEFAULT, STGM.STGM_READ, out _);
        try
        {
            using var ms = new MemoryStream();
            CopyStreamSync(stream, ms, 0, null, CancellationToken.None);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        finally { Marshal.ReleaseComObject(stream); }
    }

    private static void CopyStreamSync(
        IStream source, Stream destination, long totalSize,
        Func<double, Task>? onProgress, CancellationToken ct)
    {
        var buffer = new byte[StreamBufferSize];
        var pcbRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            long copied = 0;
            long lastReport = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                source.Read(buffer, buffer.Length, pcbRead);
                var read = Marshal.ReadInt32(pcbRead);
                if (read <= 0) break;
                destination.Write(buffer, 0, read);
                copied += read;

                if (onProgress is not null)
                {
                    var now = Environment.TickCount64;
                    if (now - lastReport >= ProgressThrottleMs)
                    {
                        lastReport = now;
                        var percent = totalSize <= 0 ? 1d : Math.Min(1d, (double)copied / totalSize);
                        onProgress(percent).GetAwaiter().GetResult();
                    }
                }
            }
            if (onProgress is not null)
                onProgress(1.0).GetAwaiter().GetResult();
        }
        finally { Marshal.FreeHGlobal(pcbRead); }
    }

    private static string ExtractPnpId(string root)
        => root.StartsWith(WpdInterop.RootScheme, StringComparison.OrdinalIgnoreCase)
            ? root[WpdInterop.RootScheme.Length..]
            : root;

    private static WpdSession OpenDevice(string pnpId, bool readOnly)
    {
        var info = (PortableDeviceApi.IPortableDeviceValues)new PortableDeviceApi.PortableDeviceValues();
        info.SetStringValue(PortableDeviceApi.WPD_CLIENT_NAME, "StationCollector");
        info.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_MAJOR_VERSION, 1);
        info.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_MINOR_VERSION, 0);
        info.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_REVISION, 0);
        info.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE, 2);
        info.SetUnsignedIntegerValue(
            PortableDeviceApi.WPD_CLIENT_DESIRED_ACCESS,
            readOnly ? 0x80000000u : 0xC0000000u);

        var device = (PortableDeviceApi.IPortableDevice)new PortableDeviceApi.PortableDevice();
        device.Open(pnpId, info);
        return new WpdSession(device);
    }

    private sealed class WpdSession : IDisposable
    {
        private readonly PortableDeviceApi.IPortableDeviceKeyCollection _scanKeys;
        private readonly Dictionary<string, string> _storageNames;

        private readonly PortableDeviceApi.IPortableDeviceKeyCollection _spaceKeys;
      


        public WpdSession(PortableDeviceApi.IPortableDevice device)
        {
            Device = device;
            Content = device.Content();
            Properties = Content.Properties();
            Resources = Content.Transfer();
            _scanKeys = BuildScanKeys();
            _spaceKeys = BuildSpaceKeys();
            (var ids, var names) = EnumerateStorages(Content, Properties, _scanKeys);
            StorageIds = ids;
            _storageNames = names;
        }

        public PortableDeviceApi.IPortableDevice Device { get; }
        public PortableDeviceApi.IPortableDeviceContent Content { get; }
        public PortableDeviceApi.IPortableDeviceProperties Properties { get; }
        public PortableDeviceApi.IPortableDeviceResources Resources { get; }
        public IReadOnlyList<string> StorageIds { get; }
        public PortableDeviceApi.IPortableDeviceKeyCollection ScanKeys => _scanKeys;
        public PortableDeviceApi.IPortableDeviceKeyCollection SpaceKeys => _spaceKeys;

        public bool IsStorageId(string id)
        {
            for (int i = 0; i < StorageIds.Count; i++)
                if (string.Equals(StorageIds[i], id, StringComparison.Ordinal)) return true;
            return false;
        }

        public string GetStorageName(string id)
            => _storageNames.TryGetValue(id, out var n) ? n : "Storage";

        private static PortableDeviceApi.IPortableDeviceKeyCollection BuildScanKeys()
        {
            var keys = (PortableDeviceApi.IPortableDeviceKeyCollection)
                new PortableDeviceApi.PortableDeviceKeyCollection();
            keys.Add(PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE);
            keys.Add(PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME);
            keys.Add(PortableDeviceApi.WPD_OBJECT_NAME);
            keys.Add(PortableDeviceApi.PKEY_GenericObj_ObjectSize);
            keys.Add(PortableDeviceApi.PKEY_GenericObj_DateModified);
            keys.Add(PortableDeviceApi.WPD_STORAGE_CAPACITY);
            return keys;
        }

        private static (List<string>, Dictionary<string, string>) EnumerateStorages(
            PortableDeviceApi.IPortableDeviceContent content,
            PortableDeviceApi.IPortableDeviceProperties props,
            PortableDeviceApi.IPortableDeviceKeyCollection keys)
        {
            var ids = new List<string>();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var e = content.EnumObjects(0, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, null);
            var buffer = new string[256];
            while (true)
            {
                var hr = e.Next((uint)buffer.Length, buffer, out var fetched);
                hr.ThrowIfFailed("WPD 枚举存储失败");
                if (fetched == 0) break;

                for (var i = 0; i < fetched; i++)
                {
                    try
                    {
                        var values = props.GetValues(buffer[i], keys);
                        if (IsStorage(values))
                        {
                            ids.Add(buffer[i]);
                            names[buffer[i]] = SafeGetString(values, PortableDeviceApi.WPD_OBJECT_NAME) ?? "Storage";
                        }
                    }
                    catch { }
                }
                if (fetched < buffer.Length) break;
            }
            return (ids, names);
        }

        private static PortableDeviceApi.IPortableDeviceKeyCollection BuildSpaceKeys()
        {
            var keys = (PortableDeviceApi.IPortableDeviceKeyCollection)
                new PortableDeviceApi.PortableDeviceKeyCollection();
            keys.Add(PortableDeviceApi.WPD_STORAGE_CAPACITY);
            keys.Add(PortableDeviceApi.WPD_STORAGE_FREE_SPACE_IN_BYTES);
            return keys;
        }
        public void Dispose()
        {
            try { Device.Close(); } catch { }
            try { Marshal.ReleaseComObject(_scanKeys); } catch { }
            try { Marshal.ReleaseComObject(_spaceKeys); } catch { }
            try { Marshal.ReleaseComObject(Resources); } catch { }
            try { Marshal.ReleaseComObject(Properties); } catch { }
            try { Marshal.ReleaseComObject(Content); } catch { }
            try { Marshal.ReleaseComObject(Device); } catch { }
        }
    }
}
