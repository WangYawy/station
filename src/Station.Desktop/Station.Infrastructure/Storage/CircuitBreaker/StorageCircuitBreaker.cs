using Station.Application.Storage;

namespace Station.Infrastructure.Storage.CircuitBreaker;

/// <summary>
/// 线程安全熔断器实现。
/// 
/// 关键设计：
///   1) 用 lock 保护所有状态转换，避免计数丢失与半开惊群；
///   2) 用 Environment.TickCount64（单调时钟）判断冷却，不受系统时间跳变影响；
///   3) 半开期只放行单次探测（_halfOpenProbeInFlight 标志）；
///   4) State 属性只读快照，不触发状态转换。
/// </summary>
public sealed class StorageCircuitBreaker : IStorageCircuitBreaker
{
    private readonly int _threshold;
    private readonly long _cooldownMs;
    private readonly object _gate = new();

    private StorageCircuitState _state = StorageCircuitState.Closed;
    private int _consecutiveFailures;
    private long _openedAtTicks;           // 打开时刻（TickCount64）
    private bool _halfOpenProbeInFlight;   // 半开期探测令牌

    public string TargetName { get; }

    public event EventHandler<CircuitStateChangedEventArgs>? StateChanged;

    public StorageCircuitBreaker(string targetName, int threshold, int cooldownSeconds)
    {
        TargetName = targetName;
        _threshold = Math.Max(1, threshold);
        _cooldownMs = Math.Max(1, cooldownSeconds) * 1000L;
    }

    public StorageCircuitState State
    {
        get { lock (_gate) return _state; }
    }

    public int ConsecutiveFailures
    {
        get { lock (_gate) return _consecutiveFailures; }
    }

    public bool TryEnter()
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;

            switch (_state)
            {
                case StorageCircuitState.Closed:
                    return true;

                case StorageCircuitState.Open:
                    // 冷却未到 → 拒绝
                    if (now - _openedAtTicks < _cooldownMs) return false;

                    // 冷却结束 → 转半开，放行探测
                    SetState(StorageCircuitState.HalfOpen);
                    _halfOpenProbeInFlight = true;
                    return true;

                case StorageCircuitState.HalfOpen:
                    // 半开期只允许一个探测在飞行中
                    if (_halfOpenProbeInFlight) return false;
                    _halfOpenProbeInFlight = true;
                    return true;

                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _halfOpenProbeInFlight = false;
            _openedAtTicks = 0;

            if (_state != StorageCircuitState.Closed)
                SetState(StorageCircuitState.Closed);
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;

            // 半开期任何失败立即重新打开
            if (_state == StorageCircuitState.HalfOpen)
            {
                _halfOpenProbeInFlight = false;
                _openedAtTicks = Environment.TickCount64;
                SetState(StorageCircuitState.Open);
                return;
            }

            if (_state == StorageCircuitState.Closed && _consecutiveFailures >= _threshold)
            {
                _openedAtTicks = Environment.TickCount64;
                SetState(StorageCircuitState.Open);
            }
        }
    }

    private void SetState(StorageCircuitState newState)
    {
        var old = _state;
        if (old == newState) return;
        _state = newState;
        // 触发事件时不持锁（避免死锁）
        var args = new CircuitStateChangedEventArgs
        {
            TargetName = TargetName,
            OldState = old,
            NewState = newState,
            ConsecutiveFailures = _consecutiveFailures
        };
        _ = Task.Run(() => StateChanged?.Invoke(this, args));
    }
}
