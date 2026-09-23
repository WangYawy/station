using Microsoft.Extensions.Logging;
using Polly;
using Station.Application.Storage;

namespace Station.Infrastructure.Storage.Retry;

/// <summary>
/// 重试装饰器（最外层）：对远端调用按 Polly 策略重试。
/// 熔断异常直接透传，不重试。
/// </summary>
public sealed class RetryStorageTarget : IStorageTarget
{
    private readonly IStorageTarget _inner;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger _logger;
    private readonly RetryOptions _options;

    public RetryStorageTarget(IStorageTarget inner, RetryOptions options, ILogger logger)
    {
        _inner = inner;
        _options = options;
        _logger = logger;

        _pipeline = StorageRetryPolicies.Build(options with
        {
            OnRetry = (attempt, delay, ex) =>
                _logger.LogWarning(ex,
                    "[{Target}] 重试 {Attempt}/{Max}（延迟 {Delay}ms）：{Message}",
                    _inner.Name, attempt + 1, options.MaxAttempts, delay.TotalMilliseconds, ex?.Message)
        });
    }

    public string Name => _inner.Name;
    public StorageTargetKind Kind => _inner.Kind;

    public Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken ct) =>
        _pipeline.ExecuteAsync(
            async token => await _inner.UploadAsync(file, onProgress, token).ConfigureAwait(false),
            ct).AsTask();

    public Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken ct) =>
        _pipeline.ExecuteAsync(
            async token => await _inner.GetRemoteSizeAsync(remotePath, token).ConfigureAwait(false),
            ct).AsTask();

    public Task<RemoteDigestResult> ComputeRemoteDigestAsync(string remotePath, CancellationToken ct) =>
       _pipeline.ExecuteAsync(
           async token => await _inner.ComputeRemoteDigestAsync(remotePath, token).ConfigureAwait(false), ct).AsTask();

    public Task<RemoteSignatureResult> SignRemoteAsync(string remotePath, string privateKeyPem, CancellationToken ct) =>
       _pipeline.ExecuteAsync(
           async token => await _inner.SignRemoteAsync(remotePath, privateKeyPem, token).ConfigureAwait(false), ct).AsTask();

    /// <summary>删除：不重试（避免雪上加霜）。</summary>
    public Task DeleteAsync(string remotePath, CancellationToken ct) =>
        _inner.DeleteAsync(remotePath, ct);
}
