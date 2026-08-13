using FluentFTP;
using Microsoft.Extensions.Options;
using System.Net;
using Station.Infrastructure.Security;

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
        var password = Sm4SecretProtector.TryUnprotect(_options.FtpPassword) ?? string.Empty;
        using var ftp = new AsyncFtpClient(_options.FtpHost, _options.FtpPort)
        {
            Credentials = new NetworkCredential(_options.FtpUser ?? string.Empty, password)
        };
        await ftp.Connect(cancellationToken);
        await using var stream = file.LocalStreamFactory?.Invoke() ?? File.OpenRead(file.LocalPath);
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
        var password = Sm4SecretProtector.TryUnprotect(_options.FtpPassword) ?? string.Empty;
        using var ftp = new AsyncFtpClient(_options.FtpHost, _options.FtpPort)
        {
            Credentials = new NetworkCredential(_options.FtpUser ?? string.Empty, password)
        };
        await ftp.Connect(cancellationToken);
        var size = await ftp.GetFileSize(remotePath, 0, cancellationToken);
        await ftp.Disconnect(cancellationToken);
        return size;
    }
}
