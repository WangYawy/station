using Microsoft.Extensions.Options;

namespace Station.Application.Collecting;

/// <summary>
/// 模拟记录仪采集源（开发验证用）：在本地目录生成模拟文件，
/// 复制时按分块模拟进度与速度；Erase 清除模拟根目录。
/// </summary>
public sealed class SimulatedCollectSource : ICollectSource
{
    private static readonly (string Name, long Mb)[] Templates =
    [
        ("video_1.mp4", 8),
        ("video_2.mp4", 16),
        ("photo_1.jpg", 2),
        ("audio_1.wav", 1),
        ("notes.log", 0)
    ];

    private readonly CollectOptions _options;
    private readonly string _root;

    public SimulatedCollectSource(CollectOptions options)
    {
        _options = options;
        _root = _options.SimulatedSourceDirectory
                ?? Path.Combine(_options.CacheDirectory, "sim-recorder");
    }

    public string SourceKey => "simulated";

    public string GetRecorderRoot(CollectDeviceInfo device) => _root;

    public Task<IReadOnlyList<SourceFileInfo>> ScanAsync(
        CollectDeviceInfo device,
        CancellationToken cancellationToken)
    {
        EnsureSampleFiles();
        var list = Directory.GetFiles(_root)
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
        SourceFileInfo file,
        string destinationPath,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(_root, file.FileName);
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
            if (_options.SimulatedChunkDelayMs > 0)
            {
                await Task.Delay(_options.SimulatedChunkDelayMs, cancellationToken); // 模拟磁盘/传输耗时
            }
        }
    }

    public Task EraseAsync(CollectDeviceInfo device, CancellationToken cancellationToken)
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.GetFiles(_root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    private void EnsureSampleFiles()
    {
        Directory.CreateDirectory(_root);
        var wanted = _options.SimulatedFileCount;
        for (var i = 0; i < wanted; i++)
        {
            var template = Templates[i % Templates.Length];
            var name = i == 0 ? template.Name : $"{Path.GetFileNameWithoutExtension(template.Name)}_{i + 1}{Path.GetExtension(template.Name)}";
            var path = Path.Combine(_root, name);
            if (File.Exists(path))
            {
                continue;
            }

            if (template.Mb == 0)
            {
                File.WriteAllText(path, $"simulated log {i + 1}\n");
                continue;
            }

            using var fs = new FileStream(path, FileMode.CreateNew);
            fs.SetLength(template.Mb * 1024 * 1024);
        }
    }
}
