using Microsoft.Extensions.Options;
using Station.Application.Collecting;

namespace Station.Infrastructure.Collecting;

/// <summary>
/// 真实 UMS（U 盘模式）采集源：枚举可移动磁盘作为记录仪根目录；真实 MTP采集源：WPD实现
/// 开发/测试可用 <see cref="CollectOptions.UmsRootOverride"/> 指定目录走同一套扫描/复制/擦除逻辑。
/// </summary>
public sealed class UmsCollectSource : ICollectSource
{
    private readonly CollectOptions _options;

    public UmsCollectSource(CollectOptions options)
    {
        _options = options;
    }

    public string SourceKey => "ums";

    public string GetRecorderRoot(CollectDeviceInfo device) => ResolveRoot();

    public Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        CollectDeviceInfo device,
        CancellationToken cancellationToken)
    {
        var root = device.RootPath ?? ResolveRoot();
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<SourceFileInfo>>([]);
        }

        var list = Directory.GetFiles(root)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new SourceFileInfo(info.Name, info.Name, info.Length, info.LastWriteTimeUtc);
            })
            .OrderBy(f => f.FileName)
            .ToList();
        return Task.FromResult<IReadOnlyList<SourceFileInfo>>(list);
    }

    public async Task CopyAsync(
        CollectDeviceInfo device,
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(device.RootPath ?? ResolveRoot(), file.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using var input = File.OpenRead(sourcePath);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[_options.ChunkBytes];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            onProgress?.Invoke(file.Size == 0 ? 1d : (double)total / file.Size);
        }
    }

    public Task EraseAsync(CollectDeviceInfo device, CancellationToken cancellationToken)
    {
        var root = device.RootPath ?? ResolveRoot();
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.GetFiles(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(file); // 普通删除（不覆盖、不粉碎）
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>解析记录仪根目录：优先配置覆盖目录，否则枚举可移动磁盘；无设备时抛出明确错误。</summary>
    private string ResolveRoot()
    {
        if (!string.IsNullOrWhiteSpace(_options.UmsRootOverride))
        {
            return _options.UmsRootOverride;
        }

        var removable = DriveInfo.GetDrives()
            .FirstOrDefault(d => d.DriveType == DriveType.Removable && d.IsReady);
        if (removable is null)
        {
            throw new InvalidOperationException("未检测到 UMS 设备（请插入 U 盘/记录仪）");
        }

        return removable.RootDirectory.FullName;
    }
}
