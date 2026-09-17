using System.Buffers;
using Station.Application.Collecting;
using Station.Application.DeviceDetection;
using Station.Application.Recorders;
using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Collecting;

/// <summary>
/// 真实 UMS（U 盘模式）采集源：枚举可移动磁盘作为记录仪根目录。
///
/// 一致的公共接口约定：
///  - <see cref="SourceFileInfo.RelativePath"/> = 相对根目录的可读路径（"DCIM/Camera/xxx.jpg"），
///    持久化到 DB、供页面展示。
///  - <see cref="SourceFileInfo.ObjectPath"/> = 完整物理路径（"D:\\DCIM\\Camera\\xxx.jpg"），
///    进程内临时句柄，不入库。
///  - Scan 按 <see cref="CollectOptions.ScanFolderFilter"/> 过滤文件夹。
///  - EraseAsync 返回成功删除文件数，并清理因此变空的父目录（保留根目录下两层）。
///
/// 开发/测试可用 <see cref="CollectOptions.UmsRootOverride"/> 指定目录走同一套逻辑。
/// </summary>
public sealed class UmsCollectSource : ICollectSource, IRecorderFileStore
{
    private const char PathSeparator = '/';

    /// <summary>进度回调最小间隔（毫秒）；100ms ≈ 10 次/秒</summary>
    private const int ProgressThrottleMs = 100;
    /// <summary>流拷贝缓冲区大小</summary>
    private const int StreamBufferSize = 64 * 1024;

    private readonly CollectOptions _options;

    public UmsCollectSource(CollectOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string SourceKey => "ums";

    public string GetRecorderRoot(CollectDeviceInfo device)
    {
        // 优先用调用方传入的具体盘符（多设备场景下必须）
        if (!string.IsNullOrWhiteSpace(device.RootPath))
            return device.RootPath;

        // 单设备兼容：枚举第一个可移动盘
        if (!string.IsNullOrWhiteSpace(_options.UmsRootOverride))
            return _options.UmsRootOverride;

        var removable = DriveInfo.GetDrives()
            .FirstOrDefault(d => d.DriveType == DriveType.Removable && d.IsReady);
        if (removable is null)
            throw new InvalidOperationException("未检测到 UMS 设备（请插入 U 盘/记录仪）");

        return removable.RootDirectory.FullName;
    }

    // --------------------------- ScanAsync ---------------------------

    public Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        CollectDeviceInfo device,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = device.RootPath;
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<SourceFileInfo>>([]);
        }

        var list = new List<SourceFileInfo>();
        // 从根目录下第一层开始递归（根目录自身不作为相对路径的一部分）
        Walk(root, root, new Stack<string>(), list, cancellationToken, matchedAncestor: false);
        list.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<SourceFileInfo>>(list);
    }

    // --------------------------- CopyAsync ---------------------------

    public async Task CopyAsync(
        CollectDeviceInfo device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var root = device.RootPath;
        var sourcePath = ResolveSourcePath(root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using var input = File.OpenRead(sourcePath);
        await using var output = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, _options.ChunkBytes);

        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        try
        {
            long total = 0;
            int read;
            long lastReportMs = 0;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, StreamBufferSize), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;

                if (onProgress is not null && file.Size > 0 && total < file.Size)
                {
                    // 时间节流：最快 10 次/秒
                    var nowMs = Environment.TickCount64;
                    if (nowMs - lastReportMs >= ProgressThrottleMs)
                    {
                        lastReportMs = nowMs;
                        var progress = Math.Min(1.0, (double)total / file.Size);
                        await onProgress(progress).ConfigureAwait(false);
                    }
                }
            }

            if (onProgress is not null)
                await onProgress(1.0).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // --------------------------- EraseAsync ---------------------------

    /// <summary>
    /// 只删除清单中显式指定的文件，删除成功后再清理因此变空的父目录（保留根目录下两层）。
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

        var root = device.RootPath;
        if (!Directory.Exists(root)) return 0;

        int deleted = 0;
        var deletedFilePaths = new List<string>();

        foreach (var file in fileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sourcePath = ResolveSourcePath(root, file);
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                    deleted++;
                    deletedFilePaths.Add(file.RelativePath);
                }
            }
            catch
            {
                // 单个删除失败，继续处理后续文件
            }
        }

        if (deletedFilePaths.Count > 0)
        {
            try
            {
                CleanupEmptyParentFolders(root, deletedFilePaths, minDepth: 3, cancellationToken);
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }

        return deleted;
    }

    // --------------------------- 绑定文件 ---------------------------

    public string? ReadFile(string recorderRoot, string fileName)
    {
        var path = Path.Combine(recorderRoot, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void WriteFile(string recorderRoot, string fileName, string content)
    {
        Directory.CreateDirectory(recorderRoot);
        File.WriteAllText(Path.Combine(recorderRoot, fileName), content);
    }

    public void DeleteFile(string recorderRoot, string fileName)
    {
        var path = Path.Combine(recorderRoot, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    public static IReadOnlyList<DetectedDevice> DetectDevices(CollectOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.UmsRootOverride))
        {
            return
            [
                new DetectedDevice(
                    "UMS:override",
                    Path.GetFileName(options.UmsRootOverride.TrimEnd(Path.DirectorySeparatorChar)),
                    null,
                    ProtocolType.Ums,
                    options.UmsRootOverride)
            ];
        }

        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
            .Select(d => new DetectedDevice(
                "UMS:" + d.RootDirectory.FullName,
                string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.RootDirectory.FullName : d.VolumeLabel,
                null,
                ProtocolType.Ums,
                d.RootDirectory.FullName))
            .ToList();
    }

    // --------------------------- 递归扫描 ---------------------------

    /// <summary>
    /// 递归遍历目录树，收集匹配文件。
    /// - 存储名/根目录不作为相对路径的一部分；子目录从第一层开始入栈。
    /// - 文件夹按 ScanFolderFilter 判断；祖先命中后所有后代都收。
    /// </summary>
    private void Walk(
        string root,
        string currentDir,
        Stack<string> pathStack,
        List<SourceFileInfo> files,
        CancellationToken ct,
        bool matchedAncestor)
    {
        // 枚举子目录
        foreach (var dir in Directory.EnumerateDirectories(currentDir))
        {
            ct.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(folderName))
                continue;

            var hit = ShouldEnterFolder(folderName);
            var nowMatched = hit || matchedAncestor;

            pathStack.Push(folderName);
            Walk(root, dir, pathStack, files, ct, nowMatched);
            pathStack.Pop();
        }

        // 未在匹配子树内，文件不收集
        if (!matchedAncestor) return;

        // 枚举文件
        foreach (var path in Directory.EnumerateFiles(currentDir))
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;
            if (!IsExtensionIncluded(fileName))
                continue;

            var info = new FileInfo(path);
            var relativePath = BuildFilePath(pathStack, fileName);

            files.Add(new SourceFileInfo(
                RelativePath: relativePath,
                FileName: fileName,
                Size: info.Length,
                ModifiedAt: info.CreationTimeUtc,
                ObjectPath: info.FullName));
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
        if (_options.FileExtensions == null || _options.FileExtensions.Count == 0) return true;
        var ext = Path.GetExtension(fileName);
        return _options.FileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    // --------------------------- 空目录清理 ---------------------------

    /// <summary>
    /// 清理因删除文件而变为空的父目录。
    /// 规则：仅删除【相对根目录深度 >= minDepth】的空目录；前两层（根下第 1、2 层）保留。
    /// 处理顺序：深度降序；一旦某目录非空，其祖先链全部跳过。
    /// </summary>
    private static void CleanupEmptyParentFolders(
        string root,
        IEnumerable<string> deletedFilePaths,
        int minDepth,
        CancellationToken ct)
    {
        // 1. 收集候选父目录（相对路径形式）
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
                var fullDir = Path.Combine(root, item.Path.Replace(PathSeparator, Path.DirectorySeparatorChar));
                if (!Directory.Exists(fullDir)) continue;

                if (Directory.EnumerateFileSystemEntries(fullDir).Any())
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

                Directory.Delete(fullDir, recursive: false);
            }
            catch
            {
                // 单个清理失败不影响其他
            }
        }
    }

    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var idx = path.LastIndexOf(PathSeparator);
        return idx > 0 ? path[..idx] : null;
    }

    // --------------------------- 路径解析 ---------------------------

    /// <summary>
    /// 定位源文件物理路径：
    /// 1) 优先 ObjectPath（同进程 Scan 时填入的绝对路径）
    /// 2) 兜底：root + RelativePath 拼接
    /// </summary>
    private static string ResolveSourcePath(string root, SourceFileInfo file)
    {
        if (!string.IsNullOrEmpty(file.ObjectPath) && File.Exists(file.ObjectPath))
            return file.ObjectPath;

        var relative = file.RelativePath.Replace(PathSeparator, Path.DirectorySeparatorChar);
        return Path.Combine(root, relative);
    }
}
