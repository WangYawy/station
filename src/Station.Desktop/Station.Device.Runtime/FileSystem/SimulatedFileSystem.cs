using Microsoft.Extensions.Logging;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.FileSystem;
using Station.Device.Options;

namespace Station.Device.Runtime.FileSystem;

/// <summary>
/// 模拟文件系统：开发 / 测试用，读写本地模拟目录。
/// </summary>
public sealed class SimulatedFileSystem : IDeviceFileSystem
{
    private readonly DeviceTransportOptions _options;
    private readonly ILogger<SimulatedFileSystem> _logger;

    public SimulatedFileSystem(
        Microsoft.Extensions.Options.IOptions<DeviceTransportOptions> options,
        ILogger<SimulatedFileSystem> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ProtocolType Protocol => ProtocolType.Ums;   // 模拟源复用 UMS 协议位

    public string GetRecorderRoot(DetectedDevice device) => device.Root;

    public Task<IReadOnlyList<SourceFileInfo>> ScanAsync(DetectedDevice device, CancellationToken ct)
    {
        if (!Directory.Exists(device.Root))
            return Task.FromResult<IReadOnlyList<SourceFileInfo>>(Array.Empty<SourceFileInfo>());

        var list = new List<SourceFileInfo>();
        foreach (var path in Directory.EnumerateFiles(device.Root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            var rel = Path.GetRelativePath(device.Root, path).Replace('\\', '/');
            list.Add(new SourceFileInfo(rel, info.Name, info.Length, info.LastWriteTime));
        }
        return Task.FromResult<IReadOnlyList<SourceFileInfo>>(list);
    }

    public async Task CopyAsync(
        DetectedDevice device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken ct)
    {
        var src = Path.Combine(device.Root, file.RelativePath);
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 模拟耗时
        const int chunk = 32 * 1024;
        var total = file.Size;
        long copied = 0;

        using var s = File.OpenRead(src);
        using var d = File.Create(destinationPath);
        var buf = new byte[chunk];
        int read;
        while ((read = await s.ReadAsync(buf.AsMemory(), ct)) > 0)
        {
            await d.WriteAsync(buf.AsMemory(0, read), ct);
            copied += read;

            if (onProgress is not null && total > 0)
                await onProgress(Math.Clamp((double)copied / total, 0, 1));

            await Task.Delay(5, ct);   // 模拟慢速设备
        }
    }

    public Task<int> EraseAsync(
        DetectedDevice device,
        IEnumerable<SourceFileInfo> files,
        CancellationToken ct)
    {
        var count = 0;
        foreach (var f in files)
        {
            var path = Path.Combine(device.Root, f.RelativePath);
            if (File.Exists(path)) { File.Delete(path); count++; }
        }
        return Task.FromResult(count);
    }

    public bool FileExists(DetectedDevice device, string relativePath)
        => File.Exists(Path.Combine(device.Root, relativePath));

    public string? ReadAllText(DetectedDevice device, string relativePath)
    {
        var p = Path.Combine(device.Root, relativePath);
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }

    public void WriteAllText(DetectedDevice device, string relativePath, string content)
    {
        var p = Path.Combine(device.Root, relativePath);
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(p, content);
    }

    public void DeleteFile(DetectedDevice device, string relativePath)
    {
        var p = Path.Combine(device.Root, relativePath);
        if (File.Exists(p)) File.Delete(p);
    }
}
