using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Station.Application.Collecting;
using Station.Application.DeviceDetection;
using Station.Application.Recorders;
using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Collecting;

/// <summary>
/// 真实 MTP 采集源（Linux）：基于 libmtp（Ubuntu/Kylin/UOS 包 libmtp9）P/Invoke。
///
/// 一致的公共接口约定：
///  - <see cref="SourceFileInfo.RelativePath"/> = 设备内【可读路径】（"存储名/DCIM/Camera/xxx.jpg"），
///    持久化到 DB、供页面展示。
///  - <see cref="SourceFileInfo.ObjectPath"/> = "deviceKey\u001FobjectId"，【进程内临时句柄，不入库】。
///    Scan 阶段填充；Copy/Erase 定位 objectId 优先级：
///      1) ObjectPath 有效且属于当前设备 → O(1)
///      2) 进程内缓存 "deviceKey|relativePath" → objectId → O(1)
///      3) 兜底：按可读路径逐级反查（跨进程 / 缓存失效时）
///  - Scan 扫描【所有存储 × 所有层级】，并按 <see cref="CollectOptions.ScanFolderFilter"/> 过滤文件夹。
///  - EraseAsync 只删除清单中显式指定的文件，删除成功后再清理因此变空的父目录（保留存储下两层）。
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMtpCollectSource : ICollectSource, IRecorderFileStore
{
    private const char PartSeparator = '\u001F';
    private const char PathSeparator = '/';
    private const uint FilesAndFoldersRoot = 0xFFFFFFFF;

    private const string prefix = "MTP:";
    /// <summary>进度回调最小间隔（毫秒）；100ms ≈ 10 次/秒</summary>
    private const int ProgressThrottleMs = 100;

    private readonly CollectOptions _options;

    // ==================== objectId 缓存 ====================
    // key   = "deviceKey|relativePath"（目录/文件均缓存）
    // value = objectId（字符串化，便于统一处理）
    private static readonly ConcurrentDictionary<string, string> _objectIdCache = new();

    public LinuxMtpCollectSource(CollectOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
        cancellationToken.ThrowIfCancellationRequested();

        var root = device.RootPath ?? GetRecorderRoot(device);
        var key = ParseRoot(root);

        // 新会话：清除该设备旧缓存，保证 objectId 与会话一致
        ClearCacheForDevice(key);

        using var session = OpenByRoot(root);
        var files = new List<SourceFileInfo>();

        // 每个存储以存储名为路径根
        var storageName = session.StorageName;
        var pathStack = new Stack<string>();
        if (!string.IsNullOrWhiteSpace(storageName))
            pathStack.Push(storageName);

        Walk(session, FilesAndFoldersRoot, pathStack, files, cancellationToken, matchedAncestor: false);

        if (pathStack.Count > 0)
            pathStack.Pop();

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
        cancellationToken.ThrowIfCancellationRequested();

        var root = device.RootPath ?? GetRecorderRoot(device);
        var key = ParseRoot(root);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var session = OpenByRoot(root);
        cancellationToken.ThrowIfCancellationRequested();

        var objectId = ResolveObjectId(session, key, file);
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

        if (onProgress is not null)
            await onProgress(1.0).ConfigureAwait(false);
    }

    /// <summary>
    /// 只删除清单中显式指定的文件，删除成功后再清理因此变空的父目录（保留存储下两层）。
    /// 返回成功删除的文件数。
    /// </summary>
    public async Task<int> EraseAsync(
        CollectDeviceInfo device,
        IEnumerable<SourceFileInfo>? files,
        CancellationToken cancellationToken)
    {
        await Task.Yield();

        if (files == null) return 0;
        var fileList = files.ToList();
        if (fileList.Count == 0) return 0;

        var root = device.RootPath ?? GetRecorderRoot(device);
        var key = ParseRoot(root);
        using var session = OpenByRoot(root);

        int deleted = 0;
        var deletedFilePaths = new List<string>();

        foreach (var file in fileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var objectId = ResolveObjectId(session, key, file);
                var result = Native.LIBMTP_Delete_Object(session.Device, objectId);
                if (result == 0)
                {
                    _objectIdCache.TryRemove(GetCacheKey(key, file.RelativePath), out _);
                    deleted++;
                    deletedFilePaths.Add(file.RelativePath);
                }
            }
            catch
            {
                // 单个删除失败，继续处理后续文件
            }
        }

        // 清理因删除文件而变为空的父目录（仅删除存储下第 3 层及更深）
        if (deletedFilePaths.Count > 0)
        {
            try
            {
                CleanupEmptyParentFolders(session, key, deletedFilePaths, minDepth: 3, cancellationToken);
            }
            catch
            {
                // 清理失败不影响采集主流程
            }
        }

        return deleted;
    }

    // ==================== 绑定文件 ====================

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
                    prefix + key,
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
    private static readonly SemaphoreSlim _libmtpGate = new(1, 1);
    private MtpSession OpenByRoot(string recorderRoot)
    {
        _libmtpGate.Wait();
        try
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
                    var storageName = FirstStorageName(node);
                    return new MtpSession(list, node, FirstStorageId(node), storageName, key);
                }
            }

            Native.LIBMTP_Release_Device_List(list);
            throw new InvalidOperationException("MTP 设备已更换或未连接，与绑定根目录不一致，请重新接入记录仪");
        }
        finally { _libmtpGate.Release(); }
    }

    private static uint FirstStorageId(IntPtr device)
    {
        var info = Marshal.PtrToStructure<Native.MtpDevice>(device);
        return info.Storage == IntPtr.Zero
            ? 0
            : Marshal.PtrToStructure<Native.MtpStorage>(info.Storage).Id;
    }

    private static string FirstStorageName(IntPtr device)
    {
        var info = Marshal.PtrToStructure<Native.MtpDevice>(device);
        if (info.Storage == IntPtr.Zero)
        {
            return "Storage";
        }

        var storage = Marshal.PtrToStructure<Native.MtpStorage>(info.Storage);
        var name = storage.StorageDescription != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(storage.StorageDescription)
            : null;
        return string.IsNullOrWhiteSpace(name) ? "Storage" : name;
    }

    /// <summary>
    /// 递归遍历：构建可读路径、按 ScanFolderFilter 过滤、填充 ObjectPath 与缓存。
    /// - RelativePath = "存储名/DCIM/Camera/xxx.jpg"
    /// - ObjectPath   = "deviceKey\u001FobjectId"
    /// </summary>
    private void Walk(
        MtpSession session,
        uint parentId,
        Stack<string> pathStack,
        List<SourceFileInfo> files,
        CancellationToken ct,
        bool matchedAncestor)
    {
        var list = Native.LIBMTP_Get_Files_And_Folders(session.Device, session.StorageId, parentId);
        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                ct.ThrowIfCancellationRequested();
                nodes.Add(node);

                var file = Marshal.PtrToStructure<Native.MtpFile>(node);
                var name = Marshal.PtrToStringUTF8(file.FileName) ?? string.Empty;

                // 文件夹：命中关键词或已在匹配子树内则递归
                if (file.FileType == (int)Native.FileType.Folder)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var hit = ShouldEnterFolder(name);
                    var nowMatched = hit || matchedAncestor;

                    pathStack.Push(name);
                    var dirPath = BuildFilePath(pathStack, string.Empty).TrimEnd(PathSeparator);
                    _objectIdCache[GetCacheKey(session.DeviceKey, dirPath)] = file.ItemId.ToString();

                    Walk(session, file.ItemId, pathStack, files, ct, nowMatched);

                    pathStack.Pop();
                    continue;
                }

                // 文件：仅在匹配子树内收集
                if (!matchedAncestor) continue;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!IsExtensionIncluded(name)) continue;

                var relativePath = BuildFilePath(pathStack, name);
                files.Add(new SourceFileInfo(
                    RelativePath: relativePath,
                    FileName: name,
                    Size: (long)file.FileSize,
                    ModifiedAt: DateTimeOffset.FromUnixTimeSeconds(file.ModificationDate).UtcDateTime,
                    ObjectPath: $"{session.DeviceKey}{PartSeparator}{file.ItemId}"));

                _objectIdCache[GetCacheKey(session.DeviceKey, relativePath)] = file.ItemId.ToString();
            }
        }
        finally
        {
            foreach (var node in nodes)
            {
                Native.LIBMTP_destroy_file_t(node);
            }
        }
    }

    private static string BuildFilePath(Stack<string> pathStack, string fileName)
    {
        if (pathStack.Count == 0)
            return fileName;

        var segments = pathStack.Reverse().ToArray();
        return string.IsNullOrEmpty(fileName)
            ? string.Join(PathSeparator, segments)
            : string.Join(PathSeparator, segments) + PathSeparator + fileName;
    }

    // ==================== 过滤 ====================

    private bool ShouldEnterFolder(string folderName)
    {
        var filter = _options.ScanFolderFilter;
        if (filter == null || filter.Count == 0) return true;
        return filter.Any(k => folderName.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsExtensionIncluded(string fileName)
    {
        if (_options.FileExtensions == null || _options.FileExtensions.Count == 0) return true;
        var ext = Path.GetExtension(fileName);
        return _options.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    // ==================== objectId 定位 ====================

    /// <summary>
    /// 三级定位：
    /// 1) ObjectPath 命中 → O(1)
    /// 2) 缓存命中 → O(1)
    /// 3) 反查（跨进程 / 缓存失效），结果回填缓存
    /// </summary>
    private static uint ResolveObjectId(MtpSession session, string key, SourceFileInfo file)
    {
        // 1) ObjectPath
        if (!string.IsNullOrEmpty(file.ObjectPath))
        {
            var idx = file.ObjectPath.IndexOf(PartSeparator);
            if (idx >= 0)
            {
                var cachedKey = file.ObjectPath[..idx];
                var idStr = file.ObjectPath[(idx + 1)..];
                if (string.Equals(cachedKey, key, StringComparison.OrdinalIgnoreCase)
                    && uint.TryParse(idStr, out var id))
                {
                    return id;
                }
            }
        }

        // 2) 缓存
        var cacheKey = GetCacheKey(key, file.RelativePath);
        if (_objectIdCache.TryGetValue(cacheKey, out var cachedIdStr)
            && uint.TryParse(cachedIdStr, out var cachedId))
        {
            return cachedId;
        }

        // 3) 反查
        var found = FindObjectByPath(session, file.RelativePath);
        if (found != 0)
        {
            _objectIdCache[cacheKey] = found.ToString();
            return found;
        }

        throw new FileNotFoundException(
            $"MTP 设备上未找到文件（已尝试反查）: {file.RelativePath}", file.RelativePath);
    }

    /// <summary>
    /// 在存储根目录（FilesAndFoldersRoot）下查找指定名称的文件。
    /// 返回指向 MtpFile 的 IntPtr（匹配节点）；调用方负责 LIBMTP_destroy_file_t 释放。
    /// 未命中返回 IntPtr.Zero。
    /// 注意：非匹配节点在 finally 中释放，仅保留匹配节点返回给调用方。
    /// </summary>
    private static IntPtr FindRootFile(MtpSession session, string fileName)
    {
        var list = Native.LIBMTP_Get_Files_And_Folders(session.Device, session.StorageId, FilesAndFoldersRoot);
        if (list == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr match = IntPtr.Zero;
        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                // 先把所有节点收集起来，避免 finally 中边遍历边释放
                nodes.Add(node);

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

    /// <summary>按可读路径逐级反查 objectId；未找到返回 0。</summary>
    private static uint FindObjectByPath(MtpSession session, string relativePath)
    {
        var parts = relativePath.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return 0;

        // 存储名匹配：去掉首段（存储名）
        var startIndex = 0;
        if (parts.Length > 0 && string.Equals(parts[0], session.StorageName, StringComparison.OrdinalIgnoreCase))
        {
            startIndex = 1;
        }

        var currentId = FilesAndFoldersRoot;
        for (var i = startIndex; i < parts.Length; i++)
        {
            var childId = FindChildByName(session, currentId, parts[i]);
            if (childId == 0) return 0;
            currentId = childId;
        }
        return currentId;
    }

    private static uint FindChildByName(MtpSession session, uint parentId, string name)
    {
        var list = Native.LIBMTP_Get_Files_And_Folders(session.Device, session.StorageId, parentId);
        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                nodes.Add(node);
                var file = Marshal.PtrToStructure<Native.MtpFile>(node);
                var fileName = Marshal.PtrToStringUTF8(file.FileName) ?? string.Empty;
                if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return file.ItemId;
                }
            }
        }
        finally
        {
            foreach (var node in nodes)
            {
                Native.LIBMTP_destroy_file_t(node);
            }
        }
        return 0;
    }

    private static string GetCacheKey(string key, string? relativePath) =>
        $"{key}|{relativePath ?? string.Empty}";

    private static void ClearCacheForDevice(string key)
    {
        var prefixKey = $"{key}|";
        foreach (var k in _objectIdCache.Keys)
        {
            if (k.StartsWith(prefixKey, StringComparison.OrdinalIgnoreCase))
            {
                _objectIdCache.TryRemove(k, out _);
            }
        }
    }

    // ==================== 空目录清理 ====================

    private void CleanupEmptyParentFolders(
        MtpSession session,
        string key,
        IEnumerable<string> deletedFilePaths,
        int minDepth,
        CancellationToken ct)
    {
        // 1. 收集候选父目录
        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in deletedFilePaths)
        {
            var parent = GetParentPath(filePath);
            while (!string.IsNullOrEmpty(parent))
            {
                candidatePaths.Add(parent);
                parent = GetParentPath(parent);
            }
        }

        // 2. 深度降序，仅保留 >= minDepth
        var orderedCandidates = candidatePaths
            .Select(p => new
            {
                Path = p,
                Depth = p.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries).Length
            })
            .Where(x => x.Depth >= minDepth)
            .OrderByDescending(x => x.Depth)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 3. 非空前缀剪枝
        var nonEmptyPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in orderedCandidates)
        {
            ct.ThrowIfCancellationRequested();

            var p = item.Path;
            var skip = false;
            while (!string.IsNullOrEmpty(p))
            {
                if (nonEmptyPrefixes.Contains(p)) { skip = true; break; }
                p = GetParentPath(p);
            }
            if (skip) continue;

            try
            {
                var folderId = FindFolderIdCached(session, key, item.Path);
                if (folderId == 0) continue;

                if (!IsFolderEmpty(session, folderId))
                {
                    p = item.Path;
                    while (!string.IsNullOrEmpty(p))
                    {
                        nonEmptyPrefixes.Add(p);
                        p = GetParentPath(p);
                    }
                    continue;
                }

                Native.LIBMTP_Delete_Object(session.Device, folderId);
                _objectIdCache.TryRemove(GetCacheKey(key, item.Path), out _);
            }
            catch
            {
                // 单个清理失败不影响其他
            }
        }
    }

    private static uint FindFolderIdCached(MtpSession session, string key, string path)
    {
        var cacheKey = GetCacheKey(key, path);
        if (_objectIdCache.TryGetValue(cacheKey, out var idStr)
            && uint.TryParse(idStr, out var id))
        {
            return id;
        }

        var found = FindObjectByPath(session, path);
        if (found != 0)
        {
            _objectIdCache[cacheKey] = found.ToString();
        }
        return found;
    }

    private static bool IsFolderEmpty(MtpSession session, uint folderId)
    {
        var list = Native.LIBMTP_Get_Files_And_Folders(session.Device, session.StorageId, folderId);
        if (list == IntPtr.Zero) return true;

        var nodes = new List<IntPtr>();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Native.FileNext(node))
            {
                nodes.Add(node);
                return false;
            }
            return true;
        }
        finally
        {
            foreach (var node in nodes)
            {
                Native.LIBMTP_destroy_file_t(node);
            }
        }
    }

    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var idx = path.LastIndexOf(PathSeparator);
        return idx > 0 ? path[..idx] : null;
    }

    // ==================== 根目录解析 ====================

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

    // ==================== 进度回调 ====================

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
            return 1; // 取消：返回非 0 中止 libmtp 传输
        }

        if (state.OnProgress is not null)
        {
            // 时间节流：除最后一帧外，最快 100ms 一次
            var nowMs = Environment.TickCount64;
            var isFinal = total > 0 && sent >= total;
            if (isFinal || nowMs - state.LastReportMs >= ProgressThrottleMs)
            {
                state.LastReportMs = nowMs;
                state.OnProgress(
                    total == 0 ? 1d : Math.Min(1d, (double)sent / total)
                ).GetAwaiter().GetResult();
            }
        }

        return 0;
    }

    /// <summary>libmtp 进度回调的状态对象（可变 LastReportMs 支持时间节流）</summary>
    private sealed class ProgressState
    {
        public ProgressState(Func<double, Task>? onProgress, long total, CancellationToken token)
        {
            OnProgress = onProgress;
            Total = total;
            Token = token;
        }

        public Func<double, Task>? OnProgress { get; }
        public long Total { get; }
        public CancellationToken Token { get; }

        /// <summary>上次回调时间（Environment.TickCount64 毫秒）；用于 100ms 节流</summary>
        public long LastReportMs;
    }

    // ==================== 会话 ====================

    private sealed class MtpSession : IDisposable
    {
        public MtpSession(IntPtr list, IntPtr device, uint storageId, string storageName, string deviceKey)
        {
            List = list;
            Device = device;
            StorageId = storageId;
            StorageName = storageName;
            DeviceKey = deviceKey;
        }

        public IntPtr List { get; }
        public IntPtr Device { get; }
        public uint StorageId { get; }
        public string StorageName { get; }
        public string DeviceKey { get; }

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
