using Station.Application.Storage;

namespace Station.Infrastructure.Storage.CircuitBreaker;

/// <summary>
/// 熔断装饰器：所有远端交互前先检查熔断状态。
/// 熔断打开时抛 <see cref="CircuitBreakerOpenException"/>，供重试装饰器识别并立即终止。
/// </summary>
public sealed class CircuitBreakerStorageTarget : IStorageTarget
{
    private readonly IStorageTarget _inner;
    private readonly IStorageCircuitBreaker _breaker;

    public CircuitBreakerStorageTarget(IStorageTarget inner, IStorageCircuitBreaker breaker)
    {
        _inner = inner;
        _breaker = breaker;
    }

    public string Name => _inner.Name;
    public StorageTargetKind Kind => _inner.Kind;

    public Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken ct) =>
        ExecuteAsync(t => _inner.UploadAsync(file, onProgress, t), ct);

    public Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken ct) =>
        ExecuteAsync(t => _inner.GetRemoteSizeAsync(remotePath, t), ct);

    public Task<RemoteDigestResult> ComputeRemoteDigestAsync(string remotePath, CancellationToken ct) =>
        ExecuteAsync(t => _inner.ComputeRemoteDigestAsync(remotePath, t), ct);

    public Task<RemoteSignatureResult> SignRemoteAsync(string remotePath, string privateKeyPem, CancellationToken ct) =>
       ExecuteAsync(t => _inner.SignRemoteAsync(remotePath, privateKeyPem, t), ct);

    public Task DeleteAsync(string remotePath, CancellationToken ct) =>
        ExecuteAsync(t => _inner.DeleteAsync(remotePath, t), ct);

    /// <summary>统一的熔断检查 + 成功/失败记账。</summary>
    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        if (!_breaker.TryEnter())
            throw new CircuitBreakerOpenException(_breaker.TargetName);

        try
        {
            await action(ct).ConfigureAwait(false);
            _breaker.RecordSuccess();
        }
        catch (OperationCanceledException)
        {
            // 取消不计入失败（用户主动）
            throw;
        }
        catch
        {
            _breaker.RecordFailure();
            throw;
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (!_breaker.TryEnter())
            throw new CircuitBreakerOpenException(_breaker.TargetName);

        try
        {
            var result = await action(ct).ConfigureAwait(false);
            _breaker.RecordSuccess();
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch { _breaker.RecordFailure(); throw; }
    }
}
