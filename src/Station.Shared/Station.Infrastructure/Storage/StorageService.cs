using Microsoft.Extensions.Logging;
using Station.Application.Storage;
using Station.Domain.Security;
using Station.Domain.Storage;

namespace Station.Infrastructure.Storage;

public sealed class StorageService : IStorageService
{
    private readonly IEnumerable<IStorageTarget> _targets;
    private readonly IStorageConfiguration _config;
    private readonly IHashService _hashService;
    private readonly ILogger<StorageService> _logger;

    public StorageService(
        IEnumerable<IStorageTarget> targets,
        IStorageConfiguration config,
        IHashService hashService,
        IDirectoryTemplateRenderer renderer,
        ILogger<StorageService> logger)
    {
        _targets = targets;
        _config = config;
        _hashService = hashService;
        _logger = logger;
    }

    public async Task<FileUploadResult> UploadFileAsync(
        string localPath,
        string remoteDirectory,
        string fileName,
        long fileSize,
        string? expectedLocalSm3,
        Func<Stream>? localStreamFactory = null,
        CancellationToken cancellationToken = default)
    {
        var remotePath = $"{remoteDirectory.TrimEnd('/')}/{fileName}";
        var errors = new List<string>();
        var anySuccess = false;
        var circuitOpen = false;

        // 如果没有提供本地 SM3，则计算
        var localSm3 = expectedLocalSm3 ?? _hashService.ComputeFileHash(localPath);

        foreach (var target in _targets)
        {
            var attempt = 0;
            var success = false;
            Exception? lastError = null;

            while (attempt < _config.RetryCount && !success)
            {
                attempt++;
                try
                {
                    await target.UploadAsync(
                        new UploadTargetFile(localPath, remotePath, fileSize, localStreamFactory),
                        null,
                        cancellationToken);

                    // 校验远端大小
                    var remoteSize = await target.GetRemoteSizeAsync(remotePath, cancellationToken);
                    if (remoteSize != fileSize)
                        throw new IOException($"远端大小不一致：期望 {fileSize}，实际 {remoteSize}");

                    // 可选 SM3 校验
                    if (_config.VerifyRemoteSm3)
                    {
                        var remoteSm3 = await target.ComputeRemoteSm3Async(remotePath, cancellationToken);
                        if (!string.Equals(localSm3, remoteSm3, StringComparison.OrdinalIgnoreCase))
                            throw new IOException($"远端 SM3 不一致：本地 {localSm3}，远端 {remoteSm3}");
                    }

                    success = true;
                    anySuccess = true;
                    _logger.LogInformation("文件 {FileName} 上传到 {Target} 成功", fileName, target.Name);
                }
                catch (CircuitBreakerOpenException)
                {
                    circuitOpen = true;
                    // 熔断打开，停止对该目标的进一步尝试
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _logger.LogWarning(ex, "文件 {FileName} 上传到 {Target} 失败（尝试 {Attempt}/{RetryCount}）",
                        fileName, target.Name, attempt, _config.RetryCount);
                    if (attempt < _config.RetryCount && _config.RetryIntervalSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(_config.RetryIntervalSeconds), cancellationToken);
                }
            }

            if (!success && !circuitOpen)
                errors.Add($"{target.Name}: {lastError?.Message}");
        }

        if (anySuccess)
            return new FileUploadResult(true, remotePath, null, 0, circuitOpen);
        else
            return new FileUploadResult(false, null, string.Join("; ", errors), _config.RetryCount, circuitOpen);
    }
}
