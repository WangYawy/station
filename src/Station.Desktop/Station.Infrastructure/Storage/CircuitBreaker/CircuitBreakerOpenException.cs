namespace Station.Infrastructure.Storage.CircuitBreaker;

/// <summary>
/// 熔断器已打开时抛出，用于让上层 StorageService 立即终止对该 target 的重试。
/// 区别于普通异常，Retry 装饰器应当识别它并停止重试。
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public string TargetName { get; }

    public CircuitBreakerOpenException(string targetName)
        : base($"存储目标 {targetName} 熔断器已打开，拒绝请求") => TargetName = targetName;

    public CircuitBreakerOpenException(string targetName, Exception inner)
        : base($"存储目标 {targetName} 熔断器已打开，拒绝请求", inner) => TargetName = targetName;
}
