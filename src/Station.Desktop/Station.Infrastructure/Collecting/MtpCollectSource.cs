//using System.Diagnostics;
//using System.Reflection;
//using System.Runtime.Versioning;
//using System.Text;
//using Nmtp;
//using Nmtp.Native;
//using Station.Application.Collecting;
//using Station.Application.DeviceDetection;
//using Station.Application.PlatformSync;
//using Station.Application.Recorders;
//using Station.Contracts;
//using Station.Domain.Collecting;
//using Vanara.PInvoke;

//namespace Station.Infrastructure.Collecting;

///// <summary>
///// 跨平台 MTP 采集源（基于 nmtp/libmtp）
///// </summary>
////[SupportedOSPlatform("windows")]
////[SupportedOSPlatform("linux")]
////[SupportedOSPlatform("osx")]
//public sealed class MtpCollectSource : ICollectSource, IRecorderFileStore
//{
//    private const char PartSeparator = '\u001F';
//    private readonly CollectOptions _options;

//    public MtpCollectSource(CollectOptions options) => _options = options;

//    public string SourceKey => "mtp";

//    public string GetRecorderRoot(CollectDeviceInfo device)
//    {
//        var (deviceId, _) = ResolveDevice();
//        return MtpRoot.RootScheme + deviceId;
//    }

//    public async Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
//        CollectDeviceInfo device,
//        CancellationToken cancellationToken)
//    {
//        await Task.Yield();
//        var (deviceId, _) = ResolveDevice();
//        using var session = OpenDevice(deviceId);
//        var list = new List<SourceFileInfo>();

//        foreach (var storage in session.GetStorages())
//        {
//            WalkObjects(session, storage.Id, parentId: 0, deviceId, list, cancellationToken);
//        }

//        return list;
//    }

//    public async Task CopyAsync(
//        CollectDeviceInfo device,
//        SourceFileInfo file,
//        string destinationPath,
//        Func<double, Task>? onProgress,
//        CancellationToken cancellationToken)
//    {
//        await Task.Yield();
//        var (deviceId, fileIdStr) = ParseRelativePath(file.RelativePath);
//        if (!uint.TryParse(fileIdStr, out var fileId))
//            throw new InvalidOperationException($"无效的文件 ID: {fileIdStr}");

//        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);

//        using var session = OpenDevice(deviceId);
//        await using var output = new System.IO.FileStream(
//            destinationPath, System.IO.FileMode.Create, System.IO.FileAccess.Write,
//            System.IO.FileShare.None, _options.ChunkBytes);

//        // 包装进度回调
//        Func<double, bool>? progress = null;
//        if (onProgress != null)
//        {
//            progress = (value) =>
//            {
//                // 异步触发进度更新，避免阻塞下载线程
//                _ = Task.Run(async () =>
//                {
//                    try
//                    {
//                        await onProgress(value).ConfigureAwait(false);
//                    }
//                    catch (Exception ex)
//                    {
//                        // 可记录日志，但不要影响下载流程（也可选择抛出，但会中断）
//                        // 这里简单忽略或记录，可酌情处理
//                    }
//                }, cancellationToken);

//                // 检查取消标志，返回 false 以取消下载
//                return !cancellationToken.IsCancellationRequested;
//            };
//        }

//        bool success = session.GetFile(fileId, progress, output);

//        if (!success)
//            throw new InvalidOperationException($"下载文件失败: {file.RelativePath}");

//        // 确保最终进度到达 100%
//        if (onProgress != null)
//            await onProgress(1.0).ConfigureAwait(false);
//    }

//    public async Task EraseAsync(CollectDeviceInfo device, CancellationToken cancellationToken)
//    {
//        await Task.Yield();
//        var (deviceId, _) = ResolveDevice();
//        using var session = OpenDevice(deviceId);
//        var files = new List<(uint ItemId, string Name)>();

//        foreach (var storage in session.GetStorages())
//        {
//            WalkObjectsForErase(session, storage.Id, parentId: 0, files, cancellationToken);
//        }

//        foreach (var (itemId, name) in files)
//        {
//            cancellationToken.ThrowIfCancellationRequested();
//            if (!_options.FileExtensions.Contains(System.IO.Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
//                continue;
//            session.DeleteObject(itemId);
//        }
//    }

//    // ---------- IRecorderFileStore ----------

//    public string? ReadFile(string recorderRoot, string fileName)
//    {
//        var (deviceId, _) = ParseRoot(recorderRoot);
//        EnsureDeviceConnected(deviceId);
//        using var session = OpenDevice(deviceId);

//        foreach (var storage in session.GetStorages())
//        {
//            var fileId = FindRootFile(session, storage.Id, fileName);
//            if (fileId.HasValue)
//                return ReadFileContent(session, fileId.Value);
//        }
//        return null;
//    }

//    public void WriteFile(string recorderRoot, string fileName, string content)
//    {
//        var (deviceId, _) = ParseRoot(recorderRoot);
//        EnsureDeviceConnected(deviceId);
//        using var session = OpenDevice(deviceId);

//        // 删除已存在的同名文件
//        foreach (var storage in session.GetStorages())
//        {
//            var existing = FindRootFile(session, storage.Id, fileName);
//            if (existing.HasValue)
//            {
//                session.DeleteObject(existing.Value);
//                break;
//            }
//        }

//        var storages = session.GetStorages().ToList();
//        if (storages.Count == 0)
//            throw new InvalidOperationException("设备没有可用存储");
//        var firstStorage = storages[0];

//        var tempPath = System.IO.Path.GetTempFileName();
//        try
//        {
//            System.IO.File.WriteAllText(tempPath, content, Encoding.UTF8);

//            // 获取底层设备指针
//            var devicePtr = GetDevicePointer(session);

//            // 构造 File 结构体
//            var fileInfo = new System.IO.FileInfo(tempPath);
//            Nmtp.File mtpFile = default;
//            mtpFile.FileName = fileName;
//            mtpFile.FileSize = (ulong)fileInfo.Length;
//            mtpFile.StorageId = firstStorage.Id;
//            mtpFile.ParentId = 0;    // 根目录
//            mtpFile.Filetype = FileType.Text; // 文本文件

//            int result = LibMtp.SendFile(
//                devicePtr,
//                tempPath,
//                ref mtpFile,
//                null,          // 进度回调
//                IntPtr.Zero    // 额外数据
//            );

//            if (result != 0)
//                throw new InvalidOperationException($"写入文件 {fileName} 失败，错误码: {result}");
//        }
//        catch (Exception ex)
//        {
//            throw new InvalidOperationException($"写入文件 {fileName} 失败：{ex.Message}");
//        }
//        finally
//        {
//            if (System.IO.File.Exists(tempPath))
//                System.IO.File.Delete(tempPath);
//        }
//    }

//    public void DeleteFile(string recorderRoot, string fileName)
//    {
//        var (deviceId, _) = ParseRoot(recorderRoot);
//        EnsureDeviceConnected(deviceId);
//        using var session = OpenDevice(deviceId);

//        foreach (var storage in session.GetStorages())
//        {
//            var fileId = FindRootFile(session, storage.Id, fileName);
//            if (fileId.HasValue)
//            {
//                session.DeleteObject(fileId.Value);
//                break;
//            }
//        }
//    }

//    // ---------- 设备检测 ----------

//    public static IReadOnlyList<DetectedDevice> DetectDevices(CollectOptions options)
//    {
//        var list = new List<DetectedDevice>();

//        using var deviceList = new RawDeviceList();
//        foreach (var raw in deviceList)
//        {
//            // 尝试两种缓存模式
//            bool opened = false;
//            bool cachedMode = true;
//            for (int attempt = 0; attempt < 2; attempt++)
//            {
//                try
//                {
//                    using var device = new Device();
//                    var rawCopy = raw;
//                    if (device.TryOpen(ref rawCopy, cachedMode))
//                    {
//                        opened = true;
//                        // 获取设备信息
//                        var id = device.GetSerialNumber() ?? raw.ToString() ?? Guid.NewGuid().ToString();
//                        var friendly = device.GetFriendlyName() ?? device.GetModelName() ?? id;

//                        list.Add(new DetectedDevice(
//                            "MTP:" + id,
//                            friendly,
//                            null,
//                            ProtocolType.Mtp,
//                            MtpRoot.RootScheme + id));
//                        break; // 成功则跳出循环
//                    }
//                }
//                catch (Exception ex)
//                {
//                    // 记录日志（此处可输出到调试或日志系统）
//                    Debug.WriteLine($"尝试打开设备失败 (cached={cachedMode}): {ex.Message}");
//                }
//                // 切换缓存模式
//                cachedMode = !cachedMode;
//                // 如果第一次失败，短暂延迟后重试（可选）
//                if (!opened && attempt == 0)
//                    Thread.Sleep(100);
//            }
//        }

//        return list;
//    }

//    // ---------- 内部辅助 ----------

//    private (string DeviceId, string Friendly) ResolveDevice()
//    {
//        using var deviceList = new RawDeviceList();
//        foreach (var raw in deviceList)
//        {
//            string id = raw.ToString() ?? string.Empty;
//            try
//            {
//                using var device = new Device();
//                var rawCopy = raw;
//                if (!device.TryOpen(ref rawCopy, cached: true))
//                    continue;

//                if (string.IsNullOrWhiteSpace(_options.MtpDeviceFilter) ||
//                    id.Contains(_options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase))
//                {
//                    return (id, id);
//                }
//            }
//            catch
//            {
//                continue;
//            }
//        }

//        throw new InvalidOperationException("未检测到 MTP 设备（请连接设备并选择 MTP 模式）");
//    }

//    private static void EnsureDeviceConnected(string deviceId)
//    {
//        using var deviceList = new RawDeviceList();
//        if (deviceList.Count() == 0)
//            throw new InvalidOperationException("未检测到 MTP 设备");

//        var found = deviceList.Any(raw => string.Equals(raw.ToString(), deviceId, StringComparison.OrdinalIgnoreCase));
//        if (!found)
//            throw new InvalidOperationException("绑定设备未连接或已更换");
//    }

//    private static Device OpenDevice(string deviceId)
//    {
//        using var deviceList = new RawDeviceList();
//        RawDevice? targetRaw = null;
//        foreach (var raw in deviceList)
//        {
//            if (string.Equals(raw.ToString(), deviceId, StringComparison.OrdinalIgnoreCase))
//            {
//                targetRaw = raw;
//                break;
//            }
//        }

//        if (targetRaw == null)
//            throw new InvalidOperationException($"设备 {deviceId} 未找到");

//        var device = new Device();
//        var rawCopy = targetRaw.Value;
//        if (!device.TryOpen(ref rawCopy, cached: true))
//        {
//            device.Dispose();
//            throw new InvalidOperationException($"无法打开设备 {deviceId}");
//        }

//        return device;
//    }

//    /// <summary>
//    /// 通过反射获取 Device 对象的底层 _device 指针（IntPtr）
//    /// </summary>
//    private static IntPtr GetDevicePointer(Device device)
//    {
//        var field = typeof(Device).GetField("_device", BindingFlags.NonPublic | BindingFlags.Instance);
//        if (field == null)
//            throw new InvalidOperationException("无法获取 Device 的底层指针");
//        return (IntPtr)field.GetValue(device)!;
//    }

//    private void WalkObjects(
//        Device session,
//        uint storageId,
//        uint parentId,
//        string deviceId,
//        List<SourceFileInfo> files,
//        CancellationToken ct)
//    {
//        var allFolders = session.GetFolderList(storageId);
//        var subFolders = allFolders.Where(f => f.ParentId == parentId);
//        foreach (var folder in subFolders)
//        {
//            ct.ThrowIfCancellationRequested();
//            WalkObjects(session, storageId, folder.FolderId, deviceId, files, ct);
//        }

//        // 获取所有文件，然后按 StorageId 和 ParentId 过滤
//        var allFiles = session.GetFiles(progress => true);
//        var currentFiles = allFiles.Where(f => f.StorageId == storageId && f.ParentId == parentId);
//        foreach (var file in currentFiles)
//        {
//            ct.ThrowIfCancellationRequested();
//            if (string.IsNullOrWhiteSpace(file.FileName))
//                continue;

//            var modified = DateTime.UnixEpoch.AddSeconds(file.ModificationDate);
//            files.Add(new SourceFileInfo(
//                $"{deviceId}{PartSeparator}{file.ItemId}",
//                file.FileName,
//                (long)file.FileSize,
//                modified.ToUniversalTime()));
//        }
//    }

//    private void WalkObjectsForErase(
//        Device session,
//        uint storageId,
//        uint parentId,
//        List<(uint ItemId, string Name)> files,
//        CancellationToken ct)
//    {
//        var allFolders = session.GetFolderList(storageId);
//        var subFolders = allFolders.Where(f => f.ParentId == parentId);
//        foreach (var folder in subFolders)
//        {
//            ct.ThrowIfCancellationRequested();
//            WalkObjectsForErase(session, storageId, folder.FolderId, files, ct);
//        }

//        var allFiles = session.GetFiles(progress => true);
//        var currentFiles = allFiles.Where(f => f.StorageId == storageId && f.ParentId == parentId);
//        foreach (var file in currentFiles)
//        {
//            ct.ThrowIfCancellationRequested();
//            if (!string.IsNullOrWhiteSpace(file.FileName))
//                files.Add((file.ItemId, file.FileName));
//        }
//    }

//    private uint? FindRootFile(Device session, uint storageId, string fileName)
//    {
//        var allFiles = session.GetFiles(progress => true);
//        var file = allFiles.FirstOrDefault(f =>
//            f.ParentId == 0 &&
//            f.StorageId == storageId &&
//            string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
//        if (file.ItemId == 0)
//            return null;
//        return file.ItemId;
//    }

//    private string ReadFileContent(Device session, uint fileId)
//    {
//        using var ms = new System.IO.MemoryStream();
//        bool success = session.GetFile(fileId, null, ms);
//        if (!success)
//            throw new InvalidOperationException($"读取文件 {fileId} 失败");
//        ms.Position = 0;
//        return Encoding.UTF8.GetString(ms.ToArray());
//    }

//    private static (string DeviceId, string ObjectId) ParseRelativePath(string relativePath)
//    {
//        var idx = relativePath.IndexOf(PartSeparator);
//        if (idx < 0)
//            throw new InvalidOperationException($"无效路径: {relativePath}");
//        return (relativePath[..idx], relativePath[(idx + 1)..]);
//    }

//    private static (string DeviceId, string Tail) ParseRoot(string recorderRoot)
//    {
//        if (!MtpRoot.IsMtpRoot(recorderRoot))
//            throw new InvalidOperationException($"不是 MTP 根: {recorderRoot}");
//        var value = recorderRoot[MtpRoot.RootScheme.Length..];
//        return (value, value);
//    }


//    // ==================== windows MTP 设备检测 处理 Windows 自身的 WPD (Windows Portable Devices) 系统服务会“抢占” MTP 设备 ====================
//    [SupportedOSPlatform("windows")]
//    public static IReadOnlyList<DetectedDevice> WinDetectDevices(CollectOptions options)
//    {
//        var list = new List<DetectedDevice>();
//        var manager = (PortableDeviceApi.IPortableDeviceManager)new PortableDeviceApi.PortableDeviceManager();
//        var deviceIds = manager.GetDevices(forceRefresh: false);
//        foreach (var id in deviceIds)
//        {
//            string friendly;
//            try
//            {
//                friendly = manager.GetDeviceFriendlyName(id) ?? string.Empty;
//            }
//            catch
//            {
//                friendly = string.Empty;
//            }

//            list.Add(new DetectedDevice(
//                "MTP:" + id,
//                string.IsNullOrWhiteSpace(friendly) ? id : friendly,
//                null,
//                ProtocolType.Mtp,
//                MtpRoot.RootScheme + id));
//        }

//        return list;
//    }

//}
