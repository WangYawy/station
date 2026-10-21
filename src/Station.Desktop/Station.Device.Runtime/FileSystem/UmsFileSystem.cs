using Microsoft.Extensions.Logging;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.FileSystem;

namespace Station.Device.Runtime.FileSystem;

/// <summary>
/// UMS（可移动磁盘）文件系统实现：本地盘符直接 IO。
/// </summary>
public sealed class UmsFileSystem : IDeviceFileSystem
{
    private const int CopyBufferSize = 64 * 1024;

    private readonly ILogger<UmsFileSystem> _logger;

    public UmsFileSystem(ILogger<UmsFileSystem> logger)
    {
        _logger = logger;
    }

    public ProtocolType Protocol => ProtocolType.Ums;

    public string GetRecorderRoot(DetectedDevice device) => device.Root;

    public Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        DetectedDevice device,
        IReadOnlyCollection<string>? extensions = null,
        IReadOnlyCollection<string>? includePathPrefixes = null,
        IReadOnlyCollection<string>? excludePathPrefixes = null,
        CancellationToken ct = default)
    {
        var root = device.Root;
        if (!Directory.Exists(root))
            return Task.FromResult<IReadOnlyList<SourceFileInfo>>(Array.Empty<SourceFileInfo>());

        var list = new List<SourceFileInfo>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');

                if (!ScanFilter.IsPathAllowed(rel, includePathPrefixes, excludePathPrefixes)) continue;
                if (!ScanFilter.IsExtensionAllowed(info.Name, extensions)) continue;

                list.Add(new SourceFileInfo(
                    RelativePath: rel,
                    FileName: info.Name,
                    Size: info.Length,
                    ModifiedAt: info.LastWriteTime));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "扫描文件失败：{Path}", path);
            }
        }
        return Task.FromResult<IReadOnlyList<SourceFileInfo>>(list);
    }

    public async Task CopyAsync(
        DetectedDevice device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken ct = default)
    {
        var sourcePath = Path.Combine(device.Root, file.RelativePath);
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        var total = file.Size;
        long copied = 0;

        using var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, CopyBufferSize, useAsync: true);
        using var dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
            FileShare.None, CopyBufferSize, useAsync: true);

        var buffer = new byte[CopyBufferSize];
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;

            if (onProgress is not null && total > 0)
                await onProgress(Math.Clamp((double)copied / total, 0, 1));
        }

        await dst.FlushAsync(ct);
        File.SetLastWriteTime(destinationPath, file.ModifiedAt);
    }

    public Task<int> EraseAsync(
        DetectedDevice device,
        IEnumerable<SourceFileInfo> files,
        CancellationToken ct = default)
    {
        var count = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var path = Path.Combine(device.Root, f.RelativePath);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "擦除文件失败：{File}", f.RelativePath);
            }
        }
        return Task.FromResult(count);
    }

    public bool FileExists(DetectedDevice device, string relativePath)
        => File.Exists(Path.Combine(device.Root, relativePath));

    public string? ReadAllText(DetectedDevice device, string relativePath)
    {
        try
        {
            var path = Path.Combine(device.Root, relativePath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读文件失败：{File}", relativePath);
            return null;
        }
    }

    public void WriteAllText(DetectedDevice device, string relativePath, string content)
    {
        var path = Path.Combine(device.Root, relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    public void DeleteFile(DetectedDevice device, string relativePath)
    {
        try
        {
            var path = Path.Combine(device.Root, relativePath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除文件失败：{File}", relativePath);
        }
    }

    public (long? AvailableBytes, long? TotalBytes) GetDeviceSpace(DetectedDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.Root)) return (null, null);

        try
        {
            // .NET 8 的 DriveInfo 在 Windows 传 "E:\"，Linux 传 "/media/user/LABEL" 均可工作
            var drive = new DriveInfo(device.Root);
            if (!drive.IsReady) return (null, null);

            // AvailableFreeSpace 是当前用户可用（考虑配额），FreeSpace 是磁盘剩余
            var available = drive.AvailableFreeSpace;
            var total = drive.TotalSize;

            if (total <= 0) return (null, null);
            return (available, total);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UMS 读取设备空间失败：{Root}", device.Root);
            return (null, null);
        }
    }
}
