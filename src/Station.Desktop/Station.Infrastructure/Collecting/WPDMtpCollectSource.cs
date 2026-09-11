using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Station.Application.Collecting;
using Station.Application.DeviceDetection;
using Station.Application.Recorders;
using Station.Contracts;
using Station.Domain.Collecting;
using Vanara.PInvoke;
using static Vanara.PInvoke.Ole32;

namespace Station.Infrastructure.Collecting;

/// <summary>
/// 真实 MTP（媒体传输协议）采集源：通过 Windows 便携设备 API（WPD）枚举/读取/擦除 MTP 记录仪。
/// 仅支持 Windows；MTP 无盘符，根目录使用虚拟路径 <c>MTP://{PnP设备ID}</c>，
/// 绑定文件（station_bind.ini）读写同样走 WPD 内容 API（设备不支持写时给出明确错误）。
///
/// ══════════════════════════════════════════════════════════════
///  方案（生产级：内存缓存 + 反查兜底）
/// ══════════════════════════════════════════════════════════════
///  - <see cref="SourceFileInfo.RelativePath"/> = 设备内【可读路径】（如 "内部共享存储/DCIM/Camera/xxx.jpg"），
///    持久化到 DB、供页面展示。
///  - <see cref="SourceFileInfo.ObjectPath"/> = "pnpId\u001FobjectId"，【进程内临时句柄，不入库】。
///    Scan 阶段填充；Copy/Erase 定位 objectId 的优先级：
///      1) ObjectPath 有效且属于当前设备 → O(1)
///      2) 进程内缓存 "pnpId|relativePath" → objectId → O(1)
///      3) 兜底：按可读路径逐级反查（仅缓存失效 / 跨进程重启后首次发生，结果回填缓存）
///    同一采集任务（Scan → DB → Copy → Erase）内【零反查】。
///  - Scan 扫描【所有存储 × 所有层级】，并按 <see cref="CollectOptions.ScanFolderFilter"/> 过滤文件夹。
/// ══════════════════════════════════════════════════════════════
///  性能优化（相对首版）
/// ══════════════════════════════════════════════════════════════
///  1. WpdSession 缓存 Properties / Transfer / 存储列表 / ScanKeys，避免重复 QI 与枚举
///  2. GetValues 传入 KeyCollection，只取扫描需要的 6 个属性，减少 COM 封送
///  3. 枚举缓冲区从 16 扩大到 256，减少 Next 调用次数
///  4. Scan 阶段目录路径也写入缓存，Erase 清理空目录时 O(1) 命中
///  5. ResolveObjectId 乐观返回（不再每次验证 ObjectExists），依赖 EnsureDeviceConnected + 失败重试兜底
///  6. CleanupEmptyParentFolders 采用"非空前缀剪枝"，避免重复枚举同一目录
///  7. CopyStreamAsync 使用 ArrayPool 复用缓冲区，降低 LOH 压力
/// ══════════════════════════════════════════════════════════════
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WPDMtpCollectSource : ICollectSource, IRecorderFileStore
{
    /// <summary>
    /// 分隔 pnpId​ 和 objectId​ "pnpId\u001FobjectId" → 内部临时句柄
    /// </summary>
    private const char PartSeparator = '\u001F';
    private const string prefix = "MTP:";

    /// <summary>可读路径分隔符（标准路径分隔符，便于上层按目录展示/过滤）</summary>
    private const char PathSeparator = '/';

    /// <summary>WPD 枚举缓冲区大小（一次 Next 拉取的 objectId 个数）</summary>
    private const int EnumBatch = 256;

    /// <summary>进度回调最小间隔（毫秒）；100ms ≈ 10 次/秒</summary>
    private const int ProgressThrottleMs = 100;

    /// <summary>流拷贝缓冲区大小</summary>
    private const int StreamBufferSize = 64 * 1024;

    private readonly CollectOptions _options;

    // ==================== objectId 缓存 ====================
    // key   = "pnpId|relativePath"（relativePath = 可读路径，目录/文件均缓存）
    // value = objectId
    // Scan 填充（文件 + 目录），Copy/Erase 优先 O(1) 命中；进程重启/设备重连后自动降级为反查。
    private static readonly ConcurrentDictionary<string, string> _objectIdCache = new();

    public WPDMtpCollectSource(CollectOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string SourceKey => "mtp";

    public string GetRecorderRoot(CollectDeviceInfo device)
    {
        var pnpId = ExtractPnpId(device);
        return MtpRoot.RootScheme + pnpId;
    }

    // ─────────────────────────── ScanAsync ───────────────────────────

    public async Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        CollectDeviceInfo device,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var pnpId = ExtractPnpId(device);
        EnsureDeviceConnected(pnpId);

        // 新会话开始：清除该设备旧缓存，保证缓存中的 objectId 与当前会话一致
        ClearCacheForDevice(pnpId);

        using var session = OpenDevice(pnpId, readOnly: true);
        var list = new List<SourceFileInfo>();

        // 扫描所有存储对象 × 所有层级（StorageIds 已在 WpdSession 构造时枚举缓存）
        var storageIds = session.StorageIds;
        foreach (var storageId in storageIds)
        {
            await WalkObjectsAsync(session, storageId, pnpId, list, cancellationToken);
        }

        // 兜底：无存储对象时，从设备根单次递归
        if (storageIds.Count == 0)
        {
            await WalkObjectsAsync(session, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, pnpId, list, cancellationToken);
        }

        return list;
    }

    // ─────────────────────────── CopyAsync ───────────────────────────

    public async Task CopyAsync(
        CollectDeviceInfo device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var pnpId = ExtractPnpId(device);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var session = OpenDevice(pnpId, readOnly: true);

        // 优先 ObjectPath / 缓存（O(1)），失效则走路径反查兜底
        var objectId = ResolveObjectId(session, pnpId, file);

        var stream = session.Resources.GetStream(
            objectId,
            PortableDeviceApi.WPD_RESOURCE_DEFAULT,
            STGM.STGM_READ,
            out _);

        try
        {
            await using var output = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, _options.ChunkBytes);
            await CopyStreamAsync(stream, output, file.Size, onProgress, cancellationToken);
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }

    // ─────────────────────────── EraseAsync ───────────────────────────

    /// <summary>
    /// 只删除【显式指定的文件清单】（建议传「已成功采集到本地」的子集），而非删除设备下所有文件。
    /// 删除成功后再清理因此变空的父目录（仅删除存储对象下第 3 层及更深，保留前两层）。
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

        var pnpId = ExtractPnpId(device);
        EnsureDeviceConnected(pnpId);

        using var session = OpenDevice(pnpId, readOnly: false);
        int deleted = 0;
        var deletedFilePaths = new List<string>();

        foreach (var file in fileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var objectId = ResolveObjectId(session, pnpId, file);
                DeleteObject(session.Content, objectId);

                // 删除成功，从缓存移除文件路径
                _objectIdCache.TryRemove(GetCacheKey(pnpId, file.RelativePath), out _);
                deleted++;
                deletedFilePaths.Add(file.RelativePath);
            }
            catch
            {
                // 单个删除失败（文件不存在/已被占用）不影响其余文件
            }
        }

        // 清理因删除文件而变为空的父目录（仅删除存储下第 3 层及更深）
        if (deletedFilePaths.Count > 0)
        {
            try
            {
                CleanupEmptyParentFolders(session, pnpId, deletedFilePaths, minDepth: 3, cancellationToken);
            }
            catch
            {
                // 清理失败不影响采集主流程
            }
        }

        return deleted;
    }

    // ==================== 绑定文件 ====================

    /// <summary>
    /// 读取绑定文件。查找策略：
    /// 1) 设备根目录直接子级查找；
    /// 2) 优先内部共享存储递归查找；
    /// 3) 全部存储对象递归查找兜底。
    /// </summary>
    public string? ReadFile(string recorderRoot, string fileName)
    {
        var (pnpId, _) = ParseRoot(recorderRoot);
        EnsureDeviceConnected(pnpId);
        using var session = OpenDevice(pnpId, readOnly: true);

        // 1. 设备根目录直接子级查找
        var hit = FindFileAtLevel(session, fileName, PortableDeviceApi.WPD_DEVICE_OBJECT_ID);
        if (hit.ObjectId is not null)
            return ReadObjectContent(session, hit.ObjectId);

        // 2. 优先内部共享存储递归查找
        foreach (var storageId in session.StorageIds)
        {
            if (IsInternalStorage(session, storageId))
            {
                hit = FindFileRecursive(session, fileName, storageId);
                if (hit.ObjectId is not null)
                    return ReadObjectContent(session, hit.ObjectId);
            }
        }

        // 3. 兜底：全部存储递归
        foreach (var storageId in session.StorageIds)
        {
            hit = FindFileRecursive(session, fileName, storageId);
            if (hit.ObjectId is not null)
                return ReadObjectContent(session, hit.ObjectId);
        }

        return null;
    }

    public void WriteFile(string recorderRoot, string fileName, string content)
    {
        var (pnpId, _) = ParseRoot(recorderRoot);
        EnsureDeviceConnected(pnpId);
        using var session = OpenDevice(pnpId, readOnly: false);

        // 写入目标父对象：优先内部共享存储根文件夹，其次任一存储，最后设备根
        var parentId = ResolveWriteParent(session);

        var (existingId, _) = FindFileRecursive(session, fileName, parentId);
        if (existingId is not null)
        {
            DeleteObject(session.Content, existingId);
        }

        var values = (PortableDeviceApi.IPortableDeviceValues)new PortableDeviceApi.PortableDeviceValues();
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_PARENT_ID, parentId);
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_NAME, fileName);
        values.SetStringValue(PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME, fileName);
        values.SetGuidValue(PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE, PortableDeviceApi.WPD_CONTENT_TYPE_DOCUMENT);
        values.SetGuidValue(PortableDeviceApi.WPD_OBJECT_FORMAT, PortableDeviceApi.WPD_OBJECT_FORMAT_TEXT);

        var bytes = Encoding.UTF8.GetBytes(content);
        try
        {
            uint optimalBufferSize = 0;
            var created = session.Content.CreateObjectWithPropertiesAndData(
                values, out var dataStream, ref optimalBufferSize);
            try
            {
                var written = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    dataStream.Write(bytes, bytes.Length, written);
                    dataStream.Commit(0); // STGC.DEFAULT
                }
                finally
                {
                    Marshal.FreeHGlobal(written);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dataStream);
            }

            _ = created;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                $"MTP 设备不支持写入绑定文件 {fileName}（部分记录仪限制 MTP 下创建文件）：{ex.Message}", ex);
        }
    }

    public void DeleteFile(string recorderRoot, string fileName)
    {
        var (pnpId, _) = ParseRoot(recorderRoot);
        EnsureDeviceConnected(pnpId);
        using var session = OpenDevice(pnpId, readOnly: false);

        var hit = FindFileAtLevel(session, fileName, PortableDeviceApi.WPD_DEVICE_OBJECT_ID);
        if (hit.ObjectId is null)
        {
            foreach (var storageId in session.StorageIds)
            {
                hit = FindFileRecursive(session, fileName, storageId);
                if (hit.ObjectId is not null)
                    break;
            }
        }

        if (hit.ObjectId is not null)
        {
            DeleteObject(session.Content, hit.ObjectId);
        }
    }

    // ==================== 设备检测 ====================

    public static IReadOnlyList<DetectedDevice> DetectDevices(CollectOptions options)
    {
        var list = new List<DetectedDevice>();
        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
        var deviceIds = manager.GetDevices(forceRefresh: false);
        foreach (var id in deviceIds)
        {
            string friendly;
            try
            {
                friendly = manager.GetDeviceFriendlyName(id) ?? string.Empty;
            }
            catch
            {
                friendly = string.Empty;
            }

            list.Add(new DetectedDevice(
                prefix + id,
                string.IsNullOrWhiteSpace(friendly) ? id : friendly,
                null,
                ProtocolType.Mtp,
                MtpRoot.RootScheme + id));
        }

        return list;
    }

    #region ---------- WPD 内部实现 ----------

    private (string PnpId, string FriendlyName) ResolveDevice()
    {
        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
        var deviceIds = ListDeviceIds(manager);
        foreach (var id in deviceIds)
        {
            string friendly;
            try
            {
                friendly = PortableDeviceApi.GetDeviceFriendlyName(manager, id) ?? string.Empty;
            }
            catch
            {
                friendly = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_options.MtpDeviceFilter) ||
                friendly.Contains(_options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase) ||
                id.Contains(_options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase))
            {
                return (id, friendly);
            }
        }

        throw new InvalidOperationException("未检测到 MTP 设备（请通过 USB 连接记录仪并选择 MTP 模式）");
    }

    /// <summary>绑定根目录中的设备必须在线：无设备或设备已更换时给出明确错误。</summary>
    private static void EnsureDeviceConnected(string pnpId)
    {
        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
        var deviceIds = ListDeviceIds(manager);
        if (deviceIds.Length == 0)
        {
            throw new InvalidOperationException("未检测到 MTP 设备（请通过 USB 连接记录仪并选择 MTP 模式）");
        }

        if (!deviceIds.Contains(pnpId, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MTP 设备已更换或未连接，与绑定根目录不一致，请重新接入记录仪");
        }
    }

    private static string[] ListDeviceIds(PortableDeviceApi.IPortableDeviceManager manager)
    {
        manager.RefreshDeviceList();
        return PortableDeviceApi.GetDevices(manager, forceRefresh: false);
    }

    private static WpdSession OpenDevice(string pnpId, bool readOnly = true)
    {
        var clientInfo = (PortableDeviceApi.IPortableDeviceValues)new PortableDeviceApi.PortableDeviceValues();
        clientInfo.SetStringValue(PortableDeviceApi.WPD_CLIENT_NAME, "StationCollector");
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_MAJOR_VERSION, 1);
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_MINOR_VERSION, 0);
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_REVISION, 0);
        clientInfo.SetUnsignedIntegerValue(PortableDeviceApi.WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE, 2);

        // 只读操作用 GENERIC_READ，写操作用 GENERIC_READ | GENERIC_WRITE
        var desiredAccess = readOnly
            ? 0x80000000u   // GENERIC_READ
            : 0xC0000000u;  // GENERIC_READ | GENERIC_WRITE
        clientInfo.SetUnsignedIntegerValue(
            PortableDeviceApi.WPD_CLIENT_DESIRED_ACCESS, desiredAccess);

        var device = (PortableDeviceApi.IPortableDevice)new PortableDeviceApi.PortableDevice();
        device.Open(pnpId, clientInfo);
        return new WpdSession(device);
    }

    // ==================== objectId 定位（三级优先级） ====================

    /// <summary>
    /// 解析 MTP 虚拟根目录（如 "MTP://{PnP设备ID}"），提取 pnpId。
    /// </summary>
    private static (string PnpId, string Tail) ParseRoot(string recorderRoot)
    {
        if (string.IsNullOrEmpty(recorderRoot))
            throw new ArgumentException("recorderRoot 不能为空", nameof(recorderRoot));

        var scheme = recorderRoot.StartsWith(MtpRoot.RootScheme, StringComparison.OrdinalIgnoreCase)
            ? MtpRoot.RootScheme
            : (recorderRoot.StartsWith("MTP:", StringComparison.OrdinalIgnoreCase) ? "MTP:" : null);

        if (scheme is null)
            throw new InvalidOperationException($"不是合法的 MTP 根路径: {recorderRoot}");

        var value = recorderRoot[scheme.Length..];
        return (value, value);
    }

    /// <summary>
    /// 定位 objectId 的优先级：
    /// 1) <see cref="SourceFileInfo.ObjectPath"/> 非空且属于当前设备 → O(1)
    /// 2) 进程内缓存 "pnpId|relativePath" → objectId → O(1)
    /// 3) 兜底：按可读路径逐级反查（仅缓存失效 / 跨进程时），结果回填缓存。
    /// 说明：不再对命中的 objectId 做额外的 ObjectExists 验证，由 EnsureDeviceConnected 与调用方失败重试兜底。
    /// </summary>
    private static string ResolveObjectId(WpdSession session, string pnpId, SourceFileInfo file)
    {
        // 1. ObjectPath（同一进程 Scan 出来的，最优路径）
        if (!string.IsNullOrEmpty(file.ObjectPath))
        {
            var (cachedPnp, objectId) = ParseMtpObjectPath(file.ObjectPath);
            if (string.Equals(cachedPnp, pnpId, StringComparison.OrdinalIgnoreCase))
                return objectId;
        }

        // 2. 查缓存（读回场景）
        var cacheKey = GetCacheKey(pnpId, file.RelativePath);
        if (_objectIdCache.TryGetValue(cacheKey, out var cachedId))
            return cachedId;

        // 3. 兜底反查（设备重连 / 进程重启后首次）
        var found = FindObjectByPath(session, pnpId, file.RelativePath);

        // 回填缓存，避免下次再反查
        if (!string.IsNullOrEmpty(found))
            _objectIdCache[cacheKey] = found;

        return found ?? throw new FileNotFoundException(
            $"MTP 设备上未找到文件（已尝试反查）: {file.RelativePath}", file.RelativePath);
    }

    /// <summary>按可读路径（如 "内部共享存储/DCIM/Camera/xxx.jpg"）逐级查找 objectId；未找到返回 null。</summary>
    private static string? FindObjectByPath(WpdSession session, string pnpId, string relativePath)
    {
        var parts = relativePath.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        // 尝试匹配首段为存储名
        foreach (var storageId in session.StorageIds)
        {
            var storageName = session.GetStorageName(storageId);
            if (string.Equals(parts[0], storageName, StringComparison.OrdinalIgnoreCase))
            {
                return WalkPath(session, storageId, parts, 1);
            }
        }

        // 未匹配存储名：从设备根开始逐级查找
        return WalkPath(session, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, parts, 0);
    }

    /// <summary>从 parentId 起，按 parts[startIndex..] 逐级匹配子对象名，返回最终 objectId；未找到返回 null。</summary>
    private static string? WalkPath(WpdSession session, string parentId, string[] parts, int startIndex)
    {
        var currentId = parentId;
        for (var i = startIndex; i < parts.Length; i++)
        {
            currentId = FindChildByName(session, currentId, parts[i]);
            if (currentId is null) return null;
        }
        return currentId;
    }

    private static string? FindChildByName(WpdSession session, string parentId, string name)
    {
        foreach (var id in EnumerateChildren(session.Content, parentId, CancellationToken.None))
        {
            var values = session.Properties.GetValues(id, session.ScanKeys);
            var childName = GetObjectName(values);
            if (string.Equals(childName, name, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    private static string GetCacheKey(string pnpId, string? relativePath) =>
        $"{pnpId}|{relativePath ?? string.Empty}";

    /// <summary>清除指定设备的全部缓存（Scan 开始时调用）。</summary>
    private static void ClearCacheForDevice(string pnpId)
    {
        var prefixKey = $"{pnpId}|";
        foreach (var key in _objectIdCache.Keys)
        {
            if (key.StartsWith(prefixKey, StringComparison.OrdinalIgnoreCase))
                _objectIdCache.TryRemove(key, out _);
        }
    }

    /// <summary>解析 "pnpId\u001FobjectId" 句柄。</summary>
    private static (string PnpId, string ObjectId) ParseMtpObjectPath(string mtpObjectPath)
    {
        if (string.IsNullOrWhiteSpace(mtpObjectPath))
            throw new ArgumentException("ObjectPath 为空", nameof(mtpObjectPath));

        var index = mtpObjectPath.IndexOf(PartSeparator);
        if (index < 0)
            throw new FormatException($"无效的 ObjectPath 格式: {mtpObjectPath}");

        return (mtpObjectPath[..index], mtpObjectPath[(index + 1)..]);
    }

    /// <summary>从可读路径首段推断 pnpId（用于跨设备清单过滤）。</summary>
    private static string? ExtractPnpIdFromPath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        var idx = relativePath.IndexOf(PathSeparator);
        return idx < 0 ? relativePath : relativePath[..idx];
    }

    // ==================== 递归扫描（构建可读路径 + 填充缓存 + ObjectPath） ====================

    /// <summary>
    /// 递归遍历对象树，收集所有文件。
    /// - RelativePath = 可读路径（如 "内部共享存储/DCIM/Camera/xxx.jpg"）→ 存 DB、页面展示
    /// - ObjectPath   = "pnpId\u001FobjectId" → 进程内临时句柄，不持久化
    /// - 目录与文件的 relativePath 均填充 _objectIdCache，供 O(1) 定位
    /// </summary>
    private async Task WalkObjectsAsync(
        WpdSession session,
        string parentObjectId,
        string pnpId,
        List<SourceFileInfo> files,
        CancellationToken cancellationToken,
        Stack<string>? pathStack = null,
        bool matchedAncestor = false)
    {
        // 顶层存储：以存储名为路径根（直接查 StorageIds 缓存，避免重复属性读取）
        var isTopLevelStorage = pathStack is null && session.IsStorageId(parentObjectId);

        pathStack ??= new Stack<string>();
        if (isTopLevelStorage)
        {
            var storageName = session.GetStorageName(parentObjectId);
            if (!string.IsNullOrWhiteSpace(storageName))
                pathStack.Push(storageName);
        }

        try
        {
            foreach (var objectId in EnumerateChildren(session.Content, parentObjectId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = session.Properties.GetValues(objectId, session.ScanKeys);

                // ── 存储对象：只下钻，绝不当文件 ──
                if (IsStorage(values))
                {
                    var storageName = GetStorageName(values);
                    var hasName = !string.IsNullOrWhiteSpace(storageName);
                    if (hasName) pathStack.Push(storageName);

                    await WalkObjectsAsync(session, objectId, pnpId, files, cancellationToken, pathStack, matchedAncestor);

                    if (hasName) pathStack.Pop();
                    continue;
                }

                // ── 文件夹/容器：判断命中并递归 ──
                if (IsContainer(values))
                {
                    var folderName = GetObjectName(values);

                    var hit = ShouldEnterFolder(values);
                    var nowMatched = hit || matchedAncestor;

                    var pushed = !string.IsNullOrWhiteSpace(folderName);
                    if (pushed)
                    {
                        // 目录路径写入缓存，Erase 清理空目录时 O(1) 命中
                        var dirPath = BuildDirPath(pathStack, folderName);
                        _objectIdCache[GetCacheKey(pnpId, dirPath)] = objectId;

                        pathStack.Push(folderName);
                    }

                    await WalkObjectsAsync(session, objectId, pnpId, files, cancellationToken, pathStack, nowMatched);

                    if (pushed) pathStack.Pop();
                    continue;
                }

                // ── 文件：仅当位于「命中目录或其命中后代」内才收集 ──
                if (matchedAncestor)
                {
                    var fileName = GetObjectName(values);
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    if (!IsExtensionIncluded(fileName))
                        continue;

                    var relativePath = BuildFilePath(pathStack, fileName);
                    var size = (long)SafeGetUInt64(values, PortableDeviceApi.PKEY_GenericObj_ObjectSize);
                    var modified = SafeGetDate(values) ?? DateTime.UtcNow;

                    var file = new SourceFileInfo(
                        RelativePath: relativePath,
                        FileName: fileName,
                        Size: size,
                        ModifiedAt: modified,
                        ObjectPath: $"{pnpId}{PartSeparator}{objectId}");

                    _objectIdCache[GetCacheKey(pnpId, relativePath)] = objectId;
                    files.Add(file);
                }
            }
        }
        finally
        {
            // 顶层存储退出时弹出存储名，保持栈平衡
            if (isTopLevelStorage && pathStack.Count > 0)
                pathStack.Pop();
        }
    }

    /// <summary>由 pathStack（根在上、子在下）与文件名拼接可读路径。</summary>
    private static string BuildFilePath(Stack<string> pathStack, string fileName)
    {
        if (pathStack.Count == 0)
            return fileName;

        var segments = pathStack.Reverse().ToArray();
        return string.Join(PathSeparator, segments) + PathSeparator + fileName;
    }

    /// <summary>由 pathStack 与目录名拼接可读路径（不含文件名）。</summary>
    private static string BuildDirPath(Stack<string> pathStack, string folderName)
    {
        if (pathStack.Count == 0)
            return folderName;

        var segments = pathStack.Reverse().ToArray();
        return string.Join(PathSeparator, segments) + PathSeparator + folderName;
    }

    // ==================== 文件夹过滤 ====================

    /// <summary>
    /// 文件夹名称关键词白名单。
    /// - ScanFolderFilter 为 null/空 → 进入所有文件夹（扫描全部）。
    /// - 否则：文件夹名【包含】任一关键词才进入其子树。
    /// </summary>
    private bool ShouldEnterFolder(PortableDeviceApi.IPortableDeviceValues values)
    {
        var filter = _options.ScanFolderFilter;
        if (filter == null || filter.Count == 0) return true;

        var folderName = GetObjectName(values);
        return filter.Any(k => folderName.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsExtensionIncluded(string fileName)
    {
        if (_options.FileExtensions == null || _options.FileExtensions.Count == 0) return true;
        var ext = Path.GetExtension(fileName);
        return _options.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    // ==================== 子对象枚举 ====================

    private static IEnumerable<string> EnumerateChildren(
        PortableDeviceApi.IPortableDeviceContent content,
        string parentObjectId,
        CancellationToken cancellationToken)
    {
        var enumerator = content.EnumObjects(0, parentObjectId, null);
        var buffer = new string[EnumBatch];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hr = enumerator.Next((uint)buffer.Length, buffer, out var fetched);
            hr.ThrowIfFailed("MTP 枚举对象失败");
            if (fetched == 0)
                yield break;

            for (var i = 0; i < fetched; i++)
                yield return buffer[i];

            if (fetched < buffer.Length)
                yield break;
        }
    }

    // ==================== 容器 / 存储判断 ====================

    private static bool IsContainer(PortableDeviceApi.IPortableDeviceValues values)
    {
        try
        {
            var contentType = values.GetGuidValue(PortableDeviceApi.WPD_OBJECT_CONTENT_TYPE);
            return contentType == PortableDeviceApi.WPD_CONTENT_TYPE_FOLDER ||
                   contentType == PortableDeviceApi.WPD_CONTENT_TYPE_FUNCTIONAL_OBJECT;
        }
        catch
        {
            if (!IsStorage(values))
            {
                return SafeGetUInt64(values, PortableDeviceApi.PKEY_GenericObj_ObjectSize) == 0 &&
                       SafeGetString(values, PortableDeviceApi.PKEY_GenericObj_ObjectFileName) is null;
            }
            return false;
        }
    }

    /// <summary>是否为存储对象（含 WPD_STORAGE_CAPACITY 属性）。</summary>
    private static bool IsStorage(PortableDeviceApi.IPortableDeviceValues values)
    {
        try
        {
            _ = values.GetUnsignedLargeIntegerValue(PortableDeviceApi.WPD_STORAGE_CAPACITY);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ==================== 文件查找（根目录 + 存储递归） ====================

    private (string? ObjectId, string? Name) FindFileAtLevel(
        WpdSession session,
        string fileName,
        string parentObjectId)
    {
        foreach (var objectId in EnumerateChildren(session.Content, parentObjectId, CancellationToken.None))
        {
            var values = session.Properties.GetValues(objectId, session.ScanKeys);

            if (IsStorage(values))
                continue;

            var name = GetObjectName(values);
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return (objectId, name);
        }
        return (null, null);
    }

    private (string? ObjectId, string? Name) FindFileRecursive(
        WpdSession session,
        string fileName,
        string rootObjectId,
        CancellationToken cancellationToken = default)
    {
        foreach (var objectId in EnumerateChildren(session.Content, rootObjectId, cancellationToken))
        {
            var values = session.Properties.GetValues(objectId, session.ScanKeys);

            if (IsStorage(values))
            {
                var r = FindFileRecursive(session, fileName, objectId, cancellationToken);
                if (r.ObjectId is not null) return r;
                continue;
            }

            if (IsContainer(values))
            {
                var r = FindFileRecursive(session, fileName, objectId, cancellationToken);
                if (r.ObjectId is not null) return r;
                continue;
            }

            var name = GetObjectName(values);
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                return (objectId, name);
        }
        return (null, null);
    }

    // ==================== 空目录清理 ====================

    /// <summary>
    /// 清理因删除文件而变为空的父目录。
    /// 规则：仅删除【存储对象下第 minDepth 层及更深】的空目录；前两层保留（如 "SD卡/DCIM/JW7137A"）。
    /// 处理顺序：按路径深度降序（深 → 浅）。一旦某层被判定非空，其所有祖先自动跳过。
    /// </summary>
    private void CleanupEmptyParentFolders(
        WpdSession session,
        string pnpId,
        IEnumerable<string> deletedFilePaths,
        int minDepth,
        CancellationToken cancellationToken)
    {
        // 1. 收集所有候选父目录（每个已删除文件的父目录 + 逐级向上，去重）
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

        // 2. 按深度降序排列，仅保留深度 >= minDepth 的候选
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

        // 3. 非空前缀剪枝：某个目录被判定非空后，其所有祖先无需再检查
        var nonEmptyPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in orderedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 祖先链上已判定非空 → 跳过
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
                // 优先从目录缓存 O(1) 命中；未命中则反查
                var folderId = FindFolderIdCached(session, pnpId, item.Path);
                if (string.IsNullOrEmpty(folderId))
                    continue;

                if (!IsFolderEmpty(session.Content, folderId, cancellationToken))
                {
                    // 标记本目录及其所有祖先为非空
                    p = item.Path;
                    while (!string.IsNullOrEmpty(p))
                    {
                        nonEmptyPrefixes.Add(p);
                        p = GetParentPath(p);
                    }
                    continue;
                }

                DeleteObject(session.Content, folderId);
                _objectIdCache.TryRemove(GetCacheKey(pnpId, item.Path), out _);
            }
            catch
            {
                // 单个目录清理失败，不影响其余目录
            }
        }
    }

    /// <summary>优先从缓存命中目录 objectId；未命中则走路径反查。</summary>
    private static string? FindFolderIdCached(WpdSession session, string pnpId, string path)
    {
        var key = GetCacheKey(pnpId, path);
        if (_objectIdCache.TryGetValue(key, out var id))
            return id;

        var found = FindObjectByPath(session, pnpId, path);
        if (!string.IsNullOrEmpty(found))
            _objectIdCache[key] = found;
        return found;
    }

    /// <summary>取父目录路径（以 '/' 分隔）；已无父级时返回 null。</summary>
    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var idx = path.LastIndexOf(PathSeparator);
        return idx > 0 ? path[..idx] : null;
    }

    /// <summary>判断对象是否为空文件夹（无任何子对象）。</summary>
    private static bool IsFolderEmpty(
        PortableDeviceApi.IPortableDeviceContent content,
        string folderObjectId,
        CancellationToken cancellationToken)
    {
        foreach (var _ in EnumerateChildren(content, folderObjectId, cancellationToken))
        {
            return false;
        }
        return true;
    }

    // ==================== 设备 / 对象辅助 ====================

    private string ExtractPnpId(CollectDeviceInfo device)
    {
        if (string.IsNullOrEmpty(device.RootPath))
            throw new InvalidOperationException("CollectDeviceInfo.RootPath 为空");

        return device.RootPath.StartsWith(MtpRoot.RootScheme)
            ? device.RootPath.Substring(MtpRoot.RootScheme.Length)
            : device.RootPath;
    }

    /// <summary>判断是否为「内部共享存储」。</summary>
    private static bool IsInternalStorage(WpdSession session, string storageId)
    {
        try
        {
            var name = session.GetStorageName(storageId) ?? string.Empty;
            return name.Contains("内部", StringComparison.OrdinalIgnoreCase)
                || name.Contains("共享", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Internal", StringComparison.OrdinalIgnoreCase)
                || name.Contains("内置", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Phone", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetStorageName(PortableDeviceApi.IPortableDeviceValues values)
    {
        return SafeGetString(values, PortableDeviceApi.WPD_OBJECT_NAME);
    }

    private static string GetObjectName(PortableDeviceApi.IPortableDeviceValues values)
    {
        return SafeGetString(values, PortableDeviceApi.WPD_OBJECT_ORIGINAL_FILE_NAME)
               ?? SafeGetString(values, PortableDeviceApi.WPD_OBJECT_NAME)
               ?? string.Empty;
    }

    /// <summary>获取存储对象下的第一个文件夹对象 id（Android 常为 "Internal Storage" / "0"）。</summary>
    private static string? GetFirstChildFolder(WpdSession session, string storageId)
    {
        foreach (var objectId in EnumerateChildren(session.Content, storageId, CancellationToken.None))
        {
            var values = session.Properties.GetValues(objectId, session.ScanKeys);
            if (IsContainer(values))
                return objectId;
        }
        return null;
    }

    /// <summary>解析写入目标父对象：优先内部共享存储根文件夹，其次任一存储，最后设备根。</summary>
    private string ResolveWriteParent(WpdSession session)
    {
        // 优先内部存储
        foreach (var storageId in session.StorageIds)
        {
            if (IsInternalStorage(session, storageId))
            {
                var rootFolder = GetFirstChildFolder(session, storageId);
                return rootFolder ?? storageId;
            }
        }

        // 兜底：任一存储的根文件夹
        foreach (var storageId in session.StorageIds)
        {
            var rootFolder = GetFirstChildFolder(session, storageId);
            if (rootFolder is not null)
                return rootFolder;
        }

        // 最后退回设备根
        return PortableDeviceApi.WPD_DEVICE_OBJECT_ID;
    }

    private static void DeleteObject(PortableDeviceApi.IPortableDeviceContent content, string objectId)
    {
        var ids = (PortableDeviceApi.IPortableDevicePropVariantCollection)new PortableDeviceApi.PortableDevicePropVariantCollection();
        var value = new Ole32.PROPVARIANT_UNMGD(objectId, VarEnum.VT_LPWSTR);
        ids.Add(in value);
        content.Delete(PortableDeviceApi.DELETE_OBJECT_OPTIONS.PORTABLE_DEVICE_DELETE_WITH_RECURSION, ids);
    }

    // ==================== 流拷贝 ====================

    private static async Task CopyStreamAsync(
        IStream source,
        Stream destination,
        long totalSize,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        var pcbRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            long copied = 0;
            long lastReportMs = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                source.Read(buffer, buffer.Length, pcbRead);
                var read = Marshal.ReadInt32(pcbRead);
                if (read <= 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                if (onProgress is not null)
                {
                    var nowMs = Environment.TickCount64;
                    if (nowMs - lastReportMs >= ProgressThrottleMs)
                    {
                        lastReportMs = nowMs;
                        await onProgress(totalSize <= 0
                            ? 1d
                            : Math.Min(1d, (double)copied / totalSize));
                    }
                }
            }
            if (onProgress is not null)
                await onProgress(1.0).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            Marshal.FreeHGlobal(pcbRead);
        }
    }

    private static void CopyStreamSync(IStream source, Stream destination, int bufferSize)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var pcbRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            while (true)
            {
                source.Read(buffer, buffer.Length, pcbRead);
                var read = Marshal.ReadInt32(pcbRead);
                if (read <= 0)
                    break;

                destination.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            Marshal.FreeHGlobal(pcbRead);
        }
    }

    // ==================== 属性读取辅助 ====================

    private static string? SafeGetString(PortableDeviceApi.IPortableDeviceValues values, Ole32.PROPERTYKEY key)
    {
        try
        {
            return values.GetStringValue(key);
        }
        catch
        {
            return null;
        }
    }

    private static ulong SafeGetUInt64(PortableDeviceApi.IPortableDeviceValues values, Ole32.PROPERTYKEY key)
    {
        try
        {
            return values.GetUnsignedLargeIntegerValue(key);
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime? SafeGetDate(PortableDeviceApi.IPortableDeviceValues values)
    {
        try
        {
            var value = values.GetValue(PortableDeviceApi.PKEY_GenericObj_DateModified).Value;
            return value switch
            {
                DateTime date => date.ToUniversalTime(),
                long ticks => new DateTime(ticks, DateTimeKind.Utc),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    // ==================== 读取内容（安全校验） ====================

    private string? ReadObjectContent(WpdSession session, string objectId)
    {
        var stream = session.Resources.GetStream(
            objectId,
            PortableDeviceApi.WPD_RESOURCE_DEFAULT,
            STGM.STGM_READ,
            out _);
        try
        {
            using var memory = new MemoryStream();
            CopyStreamSync(stream, memory, StreamBufferSize);
            return Encoding.UTF8.GetString(memory.ToArray());
        }
        finally
        {
            Marshal.ReleaseComObject(stream);
        }
    }

    // ==================== WpdSession（含性能优化缓存） ====================

    /// <summary>
    /// WPD 设备会话：构造时一次性缓存 COM 接口、Scan 属性键集合与存储列表；
    /// 释放时按逆序释放所有 COM 对象。
    /// </summary>
    private sealed class WpdSession : IDisposable
    {
        private readonly PortableDeviceApi.IPortableDeviceKeyCollection _scanKeys;
        private readonly Dictionary<string, string> _storageNames;

        public WpdSession(PortableDeviceApi.IPortableDevice device)
        {
            Device = device;
            Content = device.Content();
            Properties = Content.Properties();      // 只取一次，避免每次 content.Properties() 触发 QI
            Resources = Content.Transfer();         // 只取一次
            _scanKeys = BuildScanKeys();

            // 一次性枚举存储对象，并缓存 ID 与名称
            (var ids, var names) = EnumerateStorages(Content, Properties, _scanKeys);
            StorageIds = ids;
            _storageNames = names;
        }

        public PortableDeviceApi.IPortableDevice Device { get; }
        public PortableDeviceApi.IPortableDeviceContent Content { get; }
        public PortableDeviceApi.IPortableDeviceProperties Properties { get; }
        public PortableDeviceApi.IPortableDeviceResources Resources { get; }
        public IReadOnlyList<string> StorageIds { get; }

        /// <summary>Scan 阶段只请求这 6 个属性，减少 WPD 内部封送。</summary>
        public PortableDeviceApi.IPortableDeviceKeyCollection ScanKeys => _scanKeys;

        /// <summary>是否为已缓存的存储 objectId。</summary>
        public bool IsStorageId(string objectId)
        {
            for (int i = 0; i < StorageIds.Count; i++)
                if (string.Equals(StorageIds[i], objectId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>获取存储对象的名称（缓存命中）。</summary>
        public string GetStorageName(string storageId)
        {
            return _storageNames.TryGetValue(storageId, out var n) ? n : "Storage";
        }

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

        private static (List<string> Ids, Dictionary<string, string> Names) EnumerateStorages(
            PortableDeviceApi.IPortableDeviceContent content,
            PortableDeviceApi.IPortableDeviceProperties properties,
            PortableDeviceApi.IPortableDeviceKeyCollection keys)
        {
            var ids = new List<string>();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var enumerator = content.EnumObjects(0, PortableDeviceApi.WPD_DEVICE_OBJECT_ID, null);
            var buffer = new string[EnumBatch];
            while (true)
            {
                var hr = enumerator.Next((uint)buffer.Length, buffer, out var fetched);
                hr.ThrowIfFailed("MTP 枚举存储失败");
                if (fetched == 0) break;

                for (var i = 0; i < fetched; i++)
                {
                    var objectId = buffer[i];
                    try
                    {
                        var values = properties.GetValues(objectId, keys);
                        if (IsStorage(values))
                        {
                            ids.Add(objectId);
                            names[objectId] = SafeGetString(values, PortableDeviceApi.WPD_OBJECT_NAME) ?? "Storage";
                        }
                    }
                    catch
                    {
                        // 属性读取失败，忽略该对象
                    }
                }
                if (fetched < buffer.Length) break;
            }
            return (ids, names);
        }

        public void Dispose()
        {
            try { Device.Close(); } catch { /* 设备已拔出等情况忽略 */ }

            try { Marshal.ReleaseComObject(_scanKeys); } catch { }
            try { Marshal.ReleaseComObject(Resources); } catch { }
            try { Marshal.ReleaseComObject(Properties); } catch { }
            try { Marshal.ReleaseComObject(Content); } catch { }
            try { Marshal.ReleaseComObject(Device); } catch { }
        }
    }

    #endregion
}
