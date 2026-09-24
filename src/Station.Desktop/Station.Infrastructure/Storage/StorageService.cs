using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Application.Storage;
using Station.Infrastructure.Storage.Verify;

namespace Station.Infrastructure.Storage;

/// <summary>
/// 存储服务：
///   1) 并发限流（进程级 SemaphoreSlim）；
///   2) 多 target 并行 fan-out；
///   3) 结果按 MultiTargetMode 聚合；
///   4) 远端校验按 RemoteVerifyMode / RemoteContentMode；
///   5) 取消/失败时按配置清理半成品。
/// 
/// 【依赖原则】
///   文件摘要走 IFileCryptoService 门面；
///   本服务不感知 file_sig 策略、摘要算法、密钥等任何加密细节。
/// </summary>
public sealed class StorageService : IStorageService
{
    private readonly IReadOnlyList<IStorageTarget> _targets;
    private readonly StorageOptions _options;
    private readonly RemoteVerifier _verifier;
    private readonly SemaphoreSlim _gate;
    private readonly IFileCryptoService _fileCrypto;
    private readonly ILogger<StorageService> _logger;

    public StorageService(
        IReadOnlyList<IStorageTarget> targets,
        IOptions<StorageOptions> options,
        IFileCryptoService fileCrypto,
        RemoteVerifier verifier,
        SemaphoreSlim uploadGate,
        ILogger<StorageService> logger)
    {
        _targets = targets;
        _options = options.Value;
        _fileCrypto = fileCrypto;
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
        // =========================================================
        // 1) 确定内容摘要与算法
        //    - 调用方已提供 → 直接用（如采集阶段已算过）
        //    - 未提供 → 走门面现算
        // =========================================================
        string contentDigest;
        string digestAlgo;

        if (!string.IsNullOrEmpty(expectedDigest))
        {
            contentDigest = expectedDigest;
            digestAlgo = await _fileCrypto.GetDigestAlgorithmAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            (contentDigest, digestAlgo) = await _fileCrypto
                .ComputeDigestAsync(localPath, cancellationToken)
                .ConfigureAwait(false);
        }

        var remotePath = $"{remoteDirectory.TrimEnd('/')}/{fileName}";

        // =========================================================
        // 2) 并发限流
        // =========================================================
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = _targets.Where(t => t is not null).ToList();
            if (active.Count == 0)
                return Fail("未配置任何启用的存储目标");

            // 3) 并行 fan-out
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

    // =========================================================
    // 单 target 上传 + 校验 + 失败清理
    // =========================================================

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
            // ---- 上传 ----
            await target.UploadAsync(
                new UploadTargetFile(
                    localPath, remotePath, fileSize,
                    localStreamFactory,
                    ExpectedDigest: contentDigest,
                    DigestAlgorithm: digestAlgo),
                OnProgress, ct).ConfigureAwait(false);

            // ---- 远端校验 ----
            //   Plaintext 模式：size + 摘要（可重算）
            //   Ciphertext 模式：仅 size（远端密文与明文摘要不等）
            await _verifier.VerifyAsync(
                target, remotePath, fileSize, contentDigest,
                mode, contentMode, ct).ConfigureAwait(false);

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
            if (_options.CleanupOnCancel)
                await TryCleanupAsync(target, remotePath).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (_options.CleanupOnFailure)
                await TryCleanupAsync(target, remotePath).ConfigureAwait(false);
            _logger.LogError(ex, "[{Target}] 上传失败：{RemotePath}", target.Name, remotePath);
            return new TargetUploadResult(target.Name, false, null, sw.ElapsedMilliseconds,
                Attempts: _options.RetryCount, CircuitOpen: false,
                ErrorCode: ex.GetType().Name, ErrorMessage: ex.Message);
        }
    }

    // =========================================================
    // 结果聚合
    // =========================================================

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

        return new FileUploadResult(
            success,
            success ? remotePath : null,
            error, maxRetry, circuitOpen, results);
    }

    // =========================================================
    // 辅助
    // =========================================================

    private RemoteVerifyMode ResolveVerifyMode(string targetName)
        => _options.RemoteVerifyMode;

    private RemoteContentMode ResolveContentMode(string targetName)
        => _options.RemoteContentMode;

    private async Task TryCleanupAsync(IStorageTarget target, string remotePath)
    {
        try
        {
            await target.DeleteAsync(remotePath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[{Target}] 清理失败：{RemotePath}", target.Name, remotePath);
        }
    }

    private FileUploadResult Fail(string message)
        => new(false, null, message, 0, false, Array.Empty<TargetUploadResult>());
}
