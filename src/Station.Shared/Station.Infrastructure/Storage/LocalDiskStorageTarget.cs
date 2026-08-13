using Microsoft.Extensions.Options;

namespace Station.Infrastructure.Storage;

/// <summary>本地磁盘存储目标：复制到 LocalRoot + 远端相对路径，按分块回调进度。</summary>
public sealed class LocalDiskStorageTarget : IStorageTarget
{
    private readonly StorageOptions _options;

    public LocalDiskStorageTarget(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "local";

    public async Task UploadAsync(
        UploadTargetFile file,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var destination = Path.Combine(_options.LocalRoot, file.RemotePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var input = file.LocalStreamFactory?.Invoke() ?? File.OpenRead(file.LocalPath);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[_options.ChunkBytes];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            if (file.Size > 0)
            {
                onProgress?.Invoke((double)total / file.Size);
            }
        }

        if (file.Size > 0)
        {
            onProgress?.Invoke(1);
        }
    }

    public Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_options.LocalRoot, remotePath.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult(new FileInfo(path).Length);
    }
}
