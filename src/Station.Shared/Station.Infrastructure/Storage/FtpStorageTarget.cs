using FluentFTP;
using Microsoft.Extensions.Options;
using System.Net;
using Station.Infrastructure.Security;
using Station.Domain.Security;
using Station.Application.Storage;

namespace Station.Infrastructure.Storage;

/// <summary>FTP 存储目标：断点续传（Resume），远端校验大小。</summary>
public sealed class FtpStorageTarget : IStorageTarget
{
    private readonly StorageTargetConfig _targetConfig;
    private readonly StorageOptions _globalOptions;
    private readonly ISecretProtector _secretProtector;

    public FtpStorageTarget(StorageTargetConfig targetConfig, IOptions<StorageOptions> options, ISecretProtector secretProtector)
    {
        _targetConfig = targetConfig;
        _globalOptions = options.Value;
        _secretProtector = secretProtector;
    }

    public string Name => "ftp";

    public async Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken cancellationToken)
    {
        var password = _secretProtector.TryUnprotect(_targetConfig.FtpPassword) ?? string.Empty;
        using var ftp = new AsyncFtpClient(_targetConfig.FtpHost!, _targetConfig.FtpPort)
        {
            Credentials = new NetworkCredential(_targetConfig.FtpUser ?? string.Empty, password)
        };
        await ftp.Connect(cancellationToken);
        await using var stream = file.LocalStreamFactory?.Invoke() ?? File.OpenRead(file.LocalPath);
        var progress = new Progress<FtpProgress>(p => _ = onProgress?.Invoke(p.Progress / 100d));
        await ftp.UploadStream(stream, file.RemotePath, FtpRemoteExists.Resume, true, progress, cancellationToken);
        await ftp.Disconnect(cancellationToken);
    }

    public async Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        var password = _secretProtector.TryUnprotect(_targetConfig.FtpPassword) ?? string.Empty;
        using var ftp = new AsyncFtpClient(_targetConfig.FtpHost!, _targetConfig.FtpPort)
        {
            Credentials = new NetworkCredential(_targetConfig.FtpUser ?? string.Empty, password)
        };
        await ftp.Connect(cancellationToken);
        var size = await ftp.GetFileSize(remotePath, 0, cancellationToken);
        await ftp.Disconnect(cancellationToken);
        return size;
    }

    public async Task<string> ComputeRemoteSm3Async(string remotePath, CancellationToken cancellationToken)
    {
        var password = _secretProtector.TryUnprotect(_targetConfig.FtpPassword) ?? string.Empty;
        using var ftp = new AsyncFtpClient(_targetConfig.FtpHost!, _targetConfig.FtpPort)
        {
            Credentials = new NetworkCredential(_targetConfig.FtpUser ?? string.Empty, password)
        };
        await ftp.Connect(cancellationToken);
        await using var stream = await ftp.OpenRead(remotePath, FtpDataType.Binary, 0, true, cancellationToken);
        return Sm3Checksum.Compute(stream);
    }
}
