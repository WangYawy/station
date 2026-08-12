using FluentFTP;
using Microsoft.Extensions.Options;
using System.Net;

namespace Station.Infrastructure.Storage;

/// <summary>FTP 存储目标：断点续传（Resume），远端校验大小。</summary>
public sealed class FtpStorageTarget : IStorageTarget
{
    private readonly StorageOptions _options;

    public FtpStorageTarget(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "ftp";

    public async Task UploadAsync(
        UploadTargetFile file,
        Func<double, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        using var ftp = new AsyncFtpClient(_options.FtpHost, _options.FtpPort)
        {
            Credentials = new NetworkCredential(_options.FtpUser ?? string.Empty, _options.FtpPassword ?? string.Empty)
        };
        await ftp.Connect(cancellationToken);
        await using var stream = File.OpenRead(file.LocalPath);
        var progress = new Progress<FtpProgress>(p => _ = onProgress?.Invoke(p.Progress / 100d));
        await ftp.UploadStream(
            stream,
            file.RemotePath,
            FtpRemoteExists.Resume,
            true,
            progress,
            cancellationToken);
        await ftp.Disconnect(cancellationToken);
    }

    public async Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        using var ftp = new AsyncFtpClient(_options.FtpHost, _options.FtpPort)
        {
            Credentials = new NetworkCredential(_options.FtpUser ?? string.Empty, _options.FtpPassword ?? string.Empty)
        };
        await ftp.Connect(cancellationToken);
        var size = await ftp.GetFileSize(remotePath, 0, cancellationToken);
        await ftp.Disconnect(cancellationToken);
        return size;
    }
}
