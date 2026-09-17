using System.Buffers;
using System.Runtime.Versioning;
using System.Text;
using MediaDevices;
using Station.Application.Collecting;
using Station.Application.DeviceDetection;
using Station.Application.Recorders;
using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Collecting;

/// <summary>
/// 基于 MediaDevices 1.10.0 (Windows WPD API) 的 MTP 采集源。仅支持 Windows 平台。
///
/// 一致的公共接口约定：
///  - <see cref="SourceFileInfo.RelativePath"/> = 设备内【可读路径】（"SD卡/DCIM/Camera/xxx.jpg"），
///    持久化到 DB、供页面展示。
///  - <see cref="SourceFileInfo.ObjectPath"/> = "deviceId\u001F原始设备路径"，【进程内临时句柄，不入库】。
///    Scan 阶段填充；Copy/Erase 定位路径优先级：
///      1) ObjectPath 有效且属于当前设备 → O(1)
///      2) 兜底：由 RelativePath 反推设备路径（跨进程 / 从 DB 读回）
///  - Scan 扫描【所有存储 × 所有层级】，并按 <see cref="CollectOptions.ScanFolderFilter"/> 过滤文件夹。
///  - EraseAsync 返回成功删除文件数，并在删除后清理因此变空的父目录（保留存储下两层）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaCollectSource : ICollectSource, IRecorderFileStore
{
    private const char PartSeparator = '\u001F';
    private const char PathSeparator = '/';

    private const string prefix = "MTP:";

    private readonly CollectOptions _options;

    public MediaCollectSource(CollectOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    public string SourceKey => "mtp";

    public string GetRecorderRoot(CollectDeviceInfo device)
    {
        var (deviceId, _) = ResolveDevice();
        return MtpRoot.RootScheme + deviceId;
    }

    // --------------------------- ScanAsync ---------------------------

    public async Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        CollectDeviceInfo device,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var deviceId = ExtractDeviceId(device);
        EnsureDeviceConnected(deviceId);

        using var session = OpenDevice(deviceId);
        var list = new List<SourceFileInfo>();

        // 枚举所有存储根目录（如 "\Phone"、"\SD card"）
        var rootDirs = session.GetDirectories(@"\");
        foreach (var rootPath in rootDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var storageName = System.IO.Path.GetFileName(rootPath);
            if (string.IsNullOrWhiteSpace(storageName))
                storageName = rootPath.TrimStart('\\', '/');

            var pathStack = new Stack<string>();
            pathStack.Push(storageName);

            // 存储名本身不参与文件夹过滤；从 matchedAncestor=false 开始
            WalkObjects(session, rootPath, pathStack, deviceId, list, cancellationToken, matchedAncestor: false);

            pathStack.Pop();
        }

        return list;
    }

    // --------------------------- CopyAsync ---------------------------

    public async Task CopyAsync(
        CollectDeviceInfo device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var deviceId = ExtractDeviceId(device);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);

        using var session = OpenDevice(deviceId);
        await using var output = new System.IO.FileStream(
            destinationPath, System.IO.FileMode.Create, System.IO.FileAccess.Write,
            System.IO.FileShare.None, _options.ChunkBytes);

        var devicePath = ResolveDevicePath(file);

        // 进度流拦截 Write 调用（MediaDevices 内部会把数据写入目标流）
        var progressStream = new ProgressStream(output, file.Size, async p =>
        {
            if (onProgress != null)
                await onProgress(p).ConfigureAwait(false);
        });

        try
        {
            session.DownloadFile(devicePath, progressStream);
            await progressStream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"下载文件失败: {file.RelativePath}", ex);
        }

        if (onProgress != null)
            await onProgress(1.0);
    }

    // --------------------------- EraseAsync ---------------------------

    /// <summary>
    /// 只删除【显式指定的文件清单】（建议传「已成功采集到本地」的子集），
    /// 删除成功后再清理因此变空的父目录（仅删除存储对象下第 3 层及更深，保留前两层）。
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

        var deviceId = ExtractDeviceId(device);
        EnsureDeviceConnected(deviceId);

        using var session = OpenDevice(deviceId);
        int deleted = 0;
        var deletedFilePaths = new List<string>();

        foreach (var file in fileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var devicePath = ResolveDevicePath(file);
                session.DeleteFile(devicePath);
                deleted++;
                deletedFilePaths.Add(file.RelativePath);
            }
            catch
            {
                // 单个删除失败（文件不存在/已被占用）不影响其余文件
            }
        }

        // 清理因删除文件而变为空的父目录
        if (deletedFilePaths.Count > 0)
        {
            try
            {
                CleanupEmptyParentFolders(session, deletedFilePaths, minDepth: 3, cancellationToken);
            }
            catch
            {
                // 清理失败不影响采集主流程
            }
        }

        return deleted;
    }

    // --------------------------- IRecorderFileStore ---------------------------

    public string? ReadFile(string recorderRoot, string fileName)
    {
        var deviceId = ExtractDeviceIdFromRoot(recorderRoot);
        EnsureDeviceConnected(deviceId);
        using var session = OpenDevice(deviceId);

        var filePath = FindRootFile(session, fileName);
        if (filePath == null)
            return null;

        using var ms = new System.IO.MemoryStream();
        session.DownloadFile(filePath, ms);
        ms.Position = 0;
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void WriteFile(string recorderRoot, string fileName, string content)
    {
        var deviceId = ExtractDeviceIdFromRoot(recorderRoot);
        EnsureDeviceConnected(deviceId);
        using var session = OpenDevice(deviceId);

        // 删除已存在的同名文件
        var existing = FindRootFile(session, fileName);
        if (existing != null)
            session.DeleteFile(existing);

        // 优先内部共享存储根目录
        var rootDirs = session.GetDirectories(@"\");
        if (rootDirs.Length == 0)
            throw new InvalidOperationException("设备没有可用存储");

        var targetDir = SelectWriteRoot(rootDirs);

        var tempPath = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tempPath, content, Encoding.UTF8);
            var destPath = System.IO.Path.Combine(targetDir, fileName).Replace('\\', '/');
            session.UploadFile(tempPath, destPath);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    public void DeleteFile(string recorderRoot, string fileName)
    {
        var deviceId = ExtractDeviceIdFromRoot(recorderRoot);
        EnsureDeviceConnected(deviceId);
        using var session = OpenDevice(deviceId);

        var existing = FindRootFile(session, fileName);
        if (existing != null)
            session.DeleteFile(existing);
    }

    // --------------------------- 设备检测 ---------------------------

    public static IReadOnlyList<DetectedDevice> DetectDevices(CollectOptions options)
    {
        var list = new List<DetectedDevice>();
        var devices = MediaDevice.GetDevices();

        foreach (var device in devices)
        {
            try
            {
                device.Connect();
                var friendlyName = string.IsNullOrWhiteSpace(device.FriendlyName)
                    ? device.DeviceId ?? "Unknown MTP Device"
                    : device.FriendlyName;

                list.Add(new DetectedDevice(
                    prefix + device.DeviceId,
                    friendlyName,
                    null,
                    ProtocolType.Mtp,
                    MtpRoot.RootScheme + device.DeviceId));

                device.Disconnect();
            }
            catch
            {
                // 忽略无法连接的设备
            }
        }

        return list;
    }

    // --------------------------- 内部辅助 ---------------------------

    private (string DeviceId, string Friendly) ResolveDevice()
    {
        var devices = MediaDevice.GetDevices();
        foreach (var device in devices)
        {
            try
            {
                device.Connect();
                var id = device.DeviceId ?? string.Empty;
                var friendly = device.FriendlyName ?? string.Empty;
                device.Disconnect();

                if (string.IsNullOrWhiteSpace(_options.MtpDeviceFilter) ||
                    friendly.Contains(_options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase) ||
                    id.Contains(_options.MtpDeviceFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return (id, friendly);
                }
            }
            catch
            {
                // 跳过无法访问的设备
            }
        }

        throw new InvalidOperationException("未检测到 MTP 设备（请通过 USB 连接记录仪并选择 MTP 模式）");
    }

    private static void EnsureDeviceConnected(string deviceId)
    {
        var devices = MediaDevice.GetDevices();
        if (!devices.Any())
            throw new InvalidOperationException("未检测到 MTP 设备");

        var found = devices.Any(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (!found)
            throw new InvalidOperationException("MTP 设备已更换或未连接，与绑定根目录不一致，请重新接入记录仪");
    }

    private static MediaDeviceSession OpenDevice(string deviceId)
    {
        var device = MediaDevice.GetDevices()
            .FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (device == null)
            throw new InvalidOperationException($"未找到设备 ID: {deviceId}");

        device.Connect();
        return new MediaDeviceSession(device);
    }

    private string ExtractDeviceId(CollectDeviceInfo device)
    {
        if (string.IsNullOrEmpty(device.RootPath))
            throw new InvalidOperationException("CollectDeviceInfo.RootPath 为空");

        return device.RootPath.StartsWith(MtpRoot.RootScheme, StringComparison.OrdinalIgnoreCase)
            ? device.RootPath[MtpRoot.RootScheme.Length..]
            : device.RootPath;
    }

    private static string ExtractDeviceIdFromRoot(string recorderRoot)
    {
        if (string.IsNullOrEmpty(recorderRoot))
            throw new ArgumentException("recorderRoot 不能为空", nameof(recorderRoot));

        if (recorderRoot.StartsWith(MtpRoot.RootScheme, StringComparison.OrdinalIgnoreCase))
            return recorderRoot[MtpRoot.RootScheme.Length..];
        if (recorderRoot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return recorderRoot[prefix.Length..];

        throw new InvalidOperationException($"不是合法的 MTP 根路径: {recorderRoot}");
    }

    /// <summary>
    /// 定位设备上可用的路径：
    /// 1) 优先 ObjectPath（同一进程 Scan 出来的，含设备原样格式）
    /// 2) 兜底：由 RelativePath（"SD卡/DCIM/Camera/xxx.jpg"）反推 "\SD卡\DCIM\Camera\xxx.jpg"
    /// </summary>
    private static string ResolveDevicePath(SourceFileInfo file)
    {
        if (!string.IsNullOrEmpty(file.ObjectPath))
        {
            var idx = file.ObjectPath.IndexOf(PartSeparator);
            if (idx >= 0)
                return file.ObjectPath[(idx + 1)..];
        }

        if (string.IsNullOrEmpty(file.RelativePath))
            throw new InvalidOperationException("文件路径为空，无法定位设备路径");

        var normalized = file.RelativePath.Replace('/', '\\');
        return normalized.StartsWith('\\') ? normalized : "\\" + normalized;
    }

    // --------------------------- 递归扫描 ---------------------------

    /// <summary>
    /// 递归遍历目录树，收集所有匹配的文件。
    /// - RelativePath = 可读路径（"SD卡/DCIM/Camera/xxx.jpg"），存 DB、页面展示
    /// - ObjectPath   = "deviceId\u001F原始设备路径"，进程内临时句柄，不持久化
    /// - 存储名本身不参与过滤；子目录按 ScanFolderFilter 判断；祖先命中后所有后代都收
    /// </summary>
    private void WalkObjects(
        MediaDeviceSession session,
        string deviceParentPath,
        Stack<string> pathStack,
        string deviceId,
        List<SourceFileInfo> files,
        CancellationToken ct,
        bool matchedAncestor)
    {
        // 枚举子目录
        foreach (var dirPath in session.GetDirectories(deviceParentPath))
        {
            ct.ThrowIfCancellationRequested();

            var folderName = System.IO.Path.GetFileName(dirPath);
            if (string.IsNullOrWhiteSpace(folderName))
                continue;

            // 本目录命中关键词 → 该目录【及其全部后代】纳入收集；
            // 未命中但祖先已命中 → 仍在匹配子树内，继续递归收集。
            var hit = ShouldEnterFolder(folderName);
            var nowMatched = hit || matchedAncestor;

            pathStack.Push(folderName);
            WalkObjects(session, dirPath, pathStack, deviceId, files, ct, nowMatched);
            pathStack.Pop();
        }

        // 未在匹配子树内，文件不收集
        if (!matchedAncestor)
            return;

        // 枚举文件
        foreach (var filePath in session.GetFiles(deviceParentPath))
        {
            ct.ThrowIfCancellationRequested();

            // 先按扩展名预筛，避免无谓的 GetFileInfo COM 调用
            var nameFromPath = System.IO.Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(nameFromPath))
                continue;
            if (!IsExtensionIncluded(nameFromPath))
                continue;

            var info = session.GetFileInfo(filePath);
            if (info == null || string.IsNullOrWhiteSpace(info.Name))
                continue;

            var relativePath = BuildFilePath(pathStack, info.Name);
            var modified = info.LastWriteTime ?? DateTime.UtcNow;

            files.Add(new SourceFileInfo(
                RelativePath: relativePath,
                FileName: info.Name,
                Size: (long)info.Length,
                ModifiedAt: modified.ToUniversalTime(),
                ObjectPath: $"{deviceId}{PartSeparator}{filePath}"));
        }
    }

    private static string BuildFilePath(Stack<string> pathStack, string fileName)
    {
        if (pathStack.Count == 0)
            return fileName;

        var segments = pathStack.Reverse().ToArray();
        return string.Join(PathSeparator, segments) + PathSeparator + fileName;
    }

    // --------------------------- 过滤 ---------------------------

    private bool ShouldEnterFolder(string folderName)
    {
        var filter = _options.ScanFolderFilter;
        if (filter == null || filter.Count == 0) return true;
        return filter.Any(k => folderName.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsExtensionIncluded(string fileName)
    {
        if (_options.FileExtensions == null || _options.FileExtensions.Count == 0)
            return true;
        var ext = System.IO.Path.GetExtension(fileName);
        return _options.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    // --------------------------- 空目录清理 ---------------------------

    /// <summary>
    /// 清理因删除文件而变为空的父目录。
    /// 规则：仅删除【深度 >= minDepth】的空目录；前两层（存储名、存储下第一层）保留。
    /// 处理顺序：按深度降序（深 → 浅）。一旦某目录被判定非空，其所有祖先自动跳过。
    /// </summary>
    private void CleanupEmptyParentFolders(
        MediaDeviceSession session,
        IEnumerable<string> deletedFilePaths,
        int minDepth,
        CancellationToken ct)
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

        // 2. 按深度降序，仅保留深度 >= minDepth 的候选
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

        // 3. 非空前缀剪枝：某目录被判定非空后，其所有祖先无需再检查
        var nonEmptyPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in orderedCandidates)
        {
            ct.ThrowIfCancellationRequested();

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
                var devicePath = RelativeToDevicePath(item.Path);

                if (!session.DirectoryExists(devicePath))
                    continue;

                if (!IsDirectoryEmpty(session, devicePath))
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

                session.DeleteDirectory(devicePath, recursive: false);
            }
            catch
            {
                // 单个目录清理失败，不影响其余目录
            }
        }
    }

    private static bool IsDirectoryEmpty(MediaDeviceSession session, string devicePath)
    {
        return session.GetFiles(devicePath).Length == 0
            && session.GetDirectories(devicePath).Length == 0;
    }

    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var idx = path.LastIndexOf(PathSeparator);
        return idx > 0 ? path[..idx] : null;
    }

    private static string RelativeToDevicePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', '\\');
        return normalized.StartsWith('\\') ? normalized : "\\" + normalized;
    }

    // --------------------------- 绑定文件辅助 ---------------------------

    private string? FindRootFile(MediaDeviceSession session, string fileName)
    {
        foreach (var rootPath in session.GetDirectories(@"\"))
        {
            var files = session.GetFiles(rootPath);
            foreach (var filePath in files)
            {
                var name = System.IO.Path.GetFileName(filePath);
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                    return filePath;
            }
        }
        return null;
    }

    /// <summary>优先内部共享存储根目录；否则第一个存储根目录。</summary>
    private static string SelectWriteRoot(string[] rootDirs)
    {
        foreach (var root in rootDirs)
        {
            var name = System.IO.Path.GetFileName(root);
            if (IsInternalStorageName(name))
                return root;
        }
        return rootDirs[0];
    }

    private static bool IsInternalStorageName(string storageName)
    {
        if (string.IsNullOrWhiteSpace(storageName)) return false;
        return storageName.Contains("内部", StringComparison.OrdinalIgnoreCase)
            || storageName.Contains("共享", StringComparison.OrdinalIgnoreCase)
            || storageName.Contains("Internal", StringComparison.OrdinalIgnoreCase)
            || storageName.Contains("内置", StringComparison.OrdinalIgnoreCase)
            || storageName.Contains("Phone", StringComparison.OrdinalIgnoreCase);
    }

    // ===============================================================
    //  MediaDeviceSession：包装 MediaDevice，屏蔽 1.10.0 API 细节
    // ===============================================================

    private sealed class MediaDeviceSession : IDisposable
    {
        private readonly MediaDevice _device;

        public MediaDeviceSession(MediaDevice device) => _device = device;

        public string[] GetDirectories(string path) => _device.GetDirectories(path);
        public string[] GetFiles(string path) => _device.GetFiles(path);

        public MediaFileInfo? GetFileInfo(string path)
        {
            try { return _device.GetFileInfo(path); }
            catch { return null; }
        }

        public bool DirectoryExists(string path)
        {
            try { return _device.DirectoryExists(path); }
            catch { return false; }
        }

        public void DownloadFile(string path, System.IO.Stream stream)
            => _device.DownloadFile(path, stream);

        public void UploadFile(string localPath, string remotePath)
            => _device.UploadFile(localPath, remotePath);

        public void DeleteFile(string path)
            => _device.DeleteFile(path);

        public void DeleteDirectory(string path, bool recursive)
            => _device.DeleteDirectory(path, recursive);

        public void Dispose()
        {
            try { _device.Disconnect(); }
            catch { /* 忽略 */ }
        }
    }

    // ===============================================================
    //  ProgressStream：拦截 Write/WriteAsync 上报进度（带时间节流）
    //  MediaDevices.DownloadFile 会把数据写入目标流，因此必须拦截 Write 而非 Read。
    // ===============================================================

    private sealed class ProgressStream : System.IO.Stream
    {
        private readonly System.IO.Stream _baseStream;
        private readonly long _totalBytes;
        private readonly Func<double, Task> _progressCallback;

        private long _bytesWritten;
        private long _lastReportMs;
        private const int ReportIntervalMs = 100;

        public ProgressStream(System.IO.Stream baseStream, long totalBytes, Func<double, Task> progressCallback)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _progressCallback = progressCallback;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => _baseStream.Length;
        public override long Position { get => _baseStream.Position; set => _baseStream.Position = value; }

        public override void Flush() => _baseStream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken)
            => _baseStream.FlushAsync(cancellationToken);

        // 只读流操作转发（DownloadFile 走写入路径，Read 通常不会被调用，仅为契约完备）
        public override int Read(byte[] buffer, int offset, int count)
            => _baseStream.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _baseStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
        public override void SetLength(long value) => _baseStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _baseStream.Write(buffer, offset, count);
            _bytesWritten += count;
            ReportProgress();
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _baseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            _bytesWritten += count;
            ReportProgress();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _baseStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _bytesWritten += buffer.Length;
            ReportProgress();
        }

        private void ReportProgress()
        {
            if (_totalBytes <= 0 || _progressCallback == null)
                return;

            // 时间节流：除最后一次外，最快 10 次/秒
            if (_bytesWritten < _totalBytes)
            {
                var nowMs = Environment.TickCount64;
                if (nowMs - _lastReportMs < ReportIntervalMs)
                    return;
                _lastReportMs = nowMs;
            }

            var progress = Math.Min(1.0, (double)_bytesWritten / _totalBytes);
            _ = _progressCallback(progress);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _baseStream.Dispose();
            base.Dispose(disposing);
        }
    }
}
