namespace Station.Infrastructure.Storage;

/// <summary>
/// 存储熔断器（P0 简单版）：连续失败达阈值 → 打开，冷却期后进入半开探测，成功即关闭。
/// </summary>
public interface IStorageCircuitBreaker
{
    bool IsOpen { get; }

    void RecordSuccess();

    void RecordFailure();
}

public sealed class StorageCircuitBreaker : IStorageCircuitBreaker
{
    private readonly int _threshold;
    private readonly TimeSpan _cooldown;
    private int _consecutiveFailures;
    private DateTime? _openedAt;

    public StorageCircuitBreaker(int threshold, int cooldownSeconds)
    {
        _threshold = Math.Max(1, threshold);
        _cooldown = TimeSpan.FromSeconds(Math.Max(1, cooldownSeconds));
    }

    public bool IsOpen
    {
        get
        {
            if (_openedAt is null)
            {
                return false;
            }

            if (DateTime.Now - _openedAt >= _cooldown)
            {
                // 冷却结束 → 半开，允许一次探测
                _openedAt = null;
                return false;
            }

            return true;
        }
    }

    public void RecordSuccess()
    {
        _consecutiveFailures = 0;
        _openedAt = null;
    }

    public void RecordFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= _threshold && _openedAt is null)
        {
            _openedAt = DateTime.Now;
        }
    }
}
