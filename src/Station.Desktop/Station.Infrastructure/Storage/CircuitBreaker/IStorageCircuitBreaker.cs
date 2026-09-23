using Station.Application.Storage;

namespace Station.Infrastructure.Storage.CircuitBreaker;

/// <summary>
/// 存储熔断器（线程安全，单调时钟，半开单令牌）。
/// 
/// 状态机：
///   Closed ──连续失败达阈值──▶ Open ──冷却期满──▶ HalfOpen
///        ◀──成功──── HalfOpen ──失败──▶ Open（重新计时）
/// </summary>
public interface IStorageCircuitBreaker
{
    /// <summary>目标名称（用于日志）。</summary>
    string TargetName { get; }

    /// <summary>当前状态（只读快照，存在瞬时竞态，仅用于展示）。</summary>
    StorageCircuitState State { get; }

    /// <summary>连续失败计数（只读快照）。</summary>
    int ConsecutiveFailures { get; }

    /// <summary>
    /// 尝试进入：Closed 直接放行；Open 检查冷却；HalfOpen 只放行一个探测。
    /// 返回 true 表示允许本次调用，false 表示应抛 <see cref="CircuitBreakerOpenException"/>。
    /// </summary>
    bool TryEnter();

    /// <summary>记录成功：清零失败计数并回到 Closed。</summary>
    void RecordSuccess();

    /// <summary>记录失败：达到阈值时从 Closed → Open。</summary>
    void RecordFailure();

    /// <summary>状态变更事件（可用于写审计或告警）。</summary>
    event EventHandler<CircuitStateChangedEventArgs>? StateChanged;
}
