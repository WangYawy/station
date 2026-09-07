using Station.Domain.Storage;

namespace Station.Infrastructure.Storage;

/// <summary>
/// 熔断装饰器：包裹 IStorageTarget，在 UploadAsync 时自动应用熔断逻辑。
/// 对其他操作（GetRemoteSizeAsync、ComputeRemoteSm3Async）同样生效。
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

    public string Name => $"{_inner.Name}(circuit)";

    public async Task UploadAsync(UploadTargetFile file, Func<double, Task>? onProgress, CancellationToken cancellationToken)
    {
        if (_breaker.IsOpen)
            throw new InvalidOperationException($"存储目标 {_inner.Name} 熔断器已打开，拒绝上传");

        try
        {
            await _inner.UploadAsync(file, onProgress, cancellationToken);
            _breaker.RecordSuccess();
        }
        catch
        {
            _breaker.RecordFailure();
            throw;
        }
    }

    public async Task<long> GetRemoteSizeAsync(string remotePath, CancellationToken cancellationToken)
    {
        if (_breaker.IsOpen)
            throw new InvalidOperationException($"存储目标 {_inner.Name} 熔断器已打开");

        try
        {
            var size = await _inner.GetRemoteSizeAsync(remotePath, cancellationToken);
            _breaker.RecordSuccess();
            return size;
        }
        catch
        {
            _breaker.RecordFailure();
            throw;
        }
    }

    public async Task<string> ComputeRemoteSm3Async(string remotePath, CancellationToken cancellationToken)
    {
        if (_breaker.IsOpen)
            throw new InvalidOperationException($"存储目标 {_inner.Name} 熔断器已打开");

        try
        {
            var sm3 = await _inner.ComputeRemoteSm3Async(remotePath, cancellationToken);
            _breaker.RecordSuccess();
            return sm3;
        }
        catch
        {
            _breaker.RecordFailure();
            throw;
        }
    }
}
