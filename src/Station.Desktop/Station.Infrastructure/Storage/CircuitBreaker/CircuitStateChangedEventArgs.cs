using Station.Application.Storage;

namespace Station.Infrastructure.Storage.CircuitBreaker;

/// <summary>熔断状态变化事件参数。</summary>
public sealed class CircuitStateChangedEventArgs : EventArgs
{
    public string TargetName { get; init; } = string.Empty;
    public StorageCircuitState OldState { get; init; }
    public StorageCircuitState NewState { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
