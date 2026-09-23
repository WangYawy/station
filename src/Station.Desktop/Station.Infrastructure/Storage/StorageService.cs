using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security.Abstractions;
using Station.Application.Storage;
using Station.Domain.Security;
using Station.Infrastructure.Storage.Verify;

namespace Station.Infrastructure.Storage;

/// <summary>
/// 存储服务：
///   1) 并发限流（进程级 SemaphoreSlim）；
///   2) 多 target 并行 fan-out；
///   3) 结果按 MultiTargetMode 聚合；
///   4) 远端校验按 RemoteVerifyMode / RemoteContentMode；
///   5) 取消/失败时按配置清理半成品。
/// </summary>
public sealed class StorageService : IStorageService
{
    private readonly IReadOnlyList<IStorageTarget> _targets;
    private readonly StorageOptions _options;
    private readonly RemoteVerifier _verifier;
    private readonly SemaphoreSlim _gate;
    private readonly ICryptoPolicyService _cryptoPolicy;
    private readonly ICryptoProviderFactory _cryptoFactory;
    private readonly ILogger<StorageService> _logger;

    public StorageService(
        IReadOnlyList<IStorageTarget> targets,
        IOptions<StorageOptions> options,
        ICryptoPolicyService cryptoPolicy,
        ICryptoProviderFactory cryptoFactory,
        RemoteVerifier verifier,
        SemaphoreSlim uploadGate,
        ILogger<StorageService> logger)
    {
        _targets = targets;
        _options = options.Value;
        _cryptoPolicy = cryptoPolicy;
        _cryptoFactory = cryptoFactory;
        _verifier = verifier;
        _gate = uploadGate;
        _logger = logger;
    }

    public async Task<FileUploadResult> UploadFileAsync(
        string localPath,
        string remoteDirectory,
        string fileName,
        long fileSize,
        string? expectedDigest,
        Func<Stream>? localStreamFactory = null,
        CancellationToken cancellationToken = default)
    {
        // 1) 读 file_sig 策略决定摘要算法
        var policy = await _cryptoPolicy.GetAsync(CryptoUsage.FileSig, cancellationToken)
            .ConfigureAwait(false);
        var hasher = _cryptoFactory.GetHasher(policy.Algorithm);

        // 2) 计算本地摘要（优先用调用方传入的）
        string contentDigest;
        if (!string.IsNullOrEmpty(expectedDigest))
        {
            contentDigest = expectedDigest;
        }
        else
        {
            await using var fs = File.OpenRead(localPath);
            contentDigest = await hasher.ComputeHashAsync(fs, cancellationToken)
                .ConfigureAwait(false);
        }
        var digestAlgo = policy.Algorithm;

        var remotePath = $"{remoteDirectory.TrimEnd('/')}/{fileName}";

        // 3) 并发限流
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = _targets.Where(t => t is not null).ToList();
            if (active.Count == 0)
                return Fail("未配置任何启用的存储目标");

            // 4) 并行 fan-out
            var tasks = active.Select(t => UploadOneAsync(
                t, localPath, remotePath, fileSize,
                contentDigest, digestAlgo, localStreamFactory, cancellationToken))
                .ToArray();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return Aggregate(remotePath, results);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>单 target 上传 + 校验 + 失败清理。</summary>
    private async Task<TargetUploadResult> UploadOneAsync(
        IStorageTarget target,
        string localPath,
        string remotePath,
        long fileSize,
        string contentDigest,
        string digestAlgo,
        Func<Stream>? localStreamFactory,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var mode = ResolveVerifyMode(target.Name);
        var contentMode = ResolveContentMode(target.Name);

        Task OnProgress(double p) => Task.CompletedTask;

        try
        {
            await target.UploadAsync(
                new UploadTargetFile(
                    localPath, remotePath, fileSize,
                    localStreamFactory,
                    ExpectedDigest: contentDigest,
                    DigestAlgorithm: digestAlgo),
                OnProgress, ct).ConfigureAwait(false);

            // 远端校验：Ciphertext 模式只校验 size（无法重算明文摘要）
            if (contentMode == RemoteContentMode.Ciphertext)
            {
                await _verifier.VerifyAsync(target, remotePath, fileSize, contentDigest, mode, ct)
                   .ConfigureAwait(false);
            }
            else
            {
                await _verifier.VerifyAsync(target, remotePath, fileSize, contentDigest, mode, ct)
                    .ConfigureAwait(false);
            }

            sw.Stop();
            return new TargetUploadResult(target.Name, true, remotePath,
                sw.ElapsedMilliseconds, Attempts: 1, CircuitOpen: false,
                ErrorCode: null, ErrorMessage: null);
        }
        catch (CircuitBreaker.CircuitBreakerOpenException ex)
        {
            sw.Stop();
            _logger.LogWarning("[{Target}] 熔断打开，跳过上传：{Message}", target.Name, ex.Message);
            return new TargetUploadResult(target.Name, false, null, sw.ElapsedMilliseconds,
                Attempts: 0, CircuitOpen: true, ErrorCode: "CIRCUIT_OPEN", ErrorMessage: ex.Message);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            if (_options.CleanupOnCancel) await TryCleanupAsync(target, remotePath).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (_options.CleanupOnFailure) await TryCleanupAsync(target, remotePath).ConfigureAwait(false);
            _logger.LogError(ex, "[{Target}] 上传失败：{RemotePath}", target.Name, remotePath);
            return new TargetUploadResult(target.Name, false, null, sw.ElapsedMilliseconds,
                Attempts: _options.RetryCount, CircuitOpen: false,
                ErrorCode: ex.GetType().Name, ErrorMessage: ex.Message);
        }
    }

    private FileUploadResult Aggregate(string remotePath, TargetUploadResult[] results)
    {
        var success = _options.MultiTargetMode switch
        {
            MultiTargetMode.AllRequired => results.All(r => r.Success),
            MultiTargetMode.BestEffort => results.FirstOrDefault(r => r.Success) is not null,
            _ => results.Any(r => r.Success)
        };

        var circuitOpen = results.Any(r => r.CircuitOpen);
        var maxRetry = results.Select(r => r.Attempts).DefaultIfEmpty(0).Max();

        string? error = null;
        if (!success)
        {
            var msgs = results.Where(r => !r.Success)
                              .Select(r => $"{r.TargetName}: {r.ErrorMessage}");
            error = string.Join("; ", msgs);
        }

        return new FileUploadResult(success, success ? remotePath : null, error, maxRetry, circuitOpen, results);
    }

    private RemoteVerifyMode ResolveVerifyMode(string targetName) => _options.RemoteVerifyMode;

    private RemoteContentMode ResolveContentMode(string targetName) => _options.RemoteContentMode;

    private async Task TryCleanupAsync(IStorageTarget target, string remotePath)
    {
        try { await target.DeleteAsync(remotePath, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "[{Target}] 清理失败：{RemotePath}", target.Name, remotePath); }
    }

    private FileUploadResult Fail(string message) =>
        new(false, null, message, 0, false, Array.Empty<TargetUploadResult>());
}
