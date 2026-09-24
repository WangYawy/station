using Station.Data.Abstractions;

namespace Station.Data.IdGeneration;

/// <summary>
/// 雪花算法 ID 生成器（线程安全）。<br/>
/// 结构：符号位(1) + 时间戳(41) + 数据中心(5) + 工作机器(5) + 序列(12)。<br/>
/// 单调递增、趋势有序、无中心依赖。<br/>
/// 时钟回拨 &lt;= 5ms 时自旋等待；&gt; 5ms 直接抛异常（快速失败）。
/// </summary>
public sealed class SnowflakeIdGenerator : IIdGenerator
{
    /// <summary>起始纪元：2024-01-01 UTC（约 69 年可用）。</summary>
    private const long Epoch = 1704067200000L;

    private const int WorkerIdBits = 5;
    private const int DatacenterIdBits = 5;
    private const int SequenceBits = 12;

    private const long MaxWorkerId = -1L ^ (-1L << WorkerIdBits);          // 31
    private const long MaxDatacenterId = -1L ^ (-1L << DatacenterIdBits);  // 31

    private const int WorkerIdShift = SequenceBits;
    private const int DatacenterIdShift = SequenceBits + WorkerIdBits;
    private const int TimestampLeftShift = SequenceBits + WorkerIdBits + DatacenterIdBits;
    private const long SequenceMask = -1L ^ (-1L << SequenceBits);         // 4095

    private const long MaxClockBackwardMs = 5L;

    private readonly long _workerId;
    private readonly long _datacenterId;
    private readonly object _lock = new();

    private long _sequence;
    private long _lastTimestamp = -1L;

    public SnowflakeIdGenerator(long workerId, long datacenterId)
    {
        if (workerId < 0 || workerId > MaxWorkerId)
            throw new ArgumentOutOfRangeException(nameof(workerId),
                $"WorkerId 必须在 [0,{MaxWorkerId}] 范围内。");
        if (datacenterId < 0 || datacenterId > MaxDatacenterId)
            throw new ArgumentOutOfRangeException(nameof(datacenterId),
                $"DatacenterId 必须在 [0,{MaxDatacenterId}] 范围内。");

        _workerId = workerId;
        _datacenterId = datacenterId;
    }

    /// <inheritdoc />
    public long NewId()
    {
        lock (_lock)
        {
            var timestamp = CurrentTimeMillis();

            // 时钟回拨处理
            if (timestamp < _lastTimestamp)
            {
                var offset = _lastTimestamp - timestamp;
                if (offset <= MaxClockBackwardMs)
                {
                    Thread.Sleep((int)offset);
                    timestamp = CurrentTimeMillis();
                }
                else
                {
                    throw new InvalidOperationException(
                        $"检测到时钟回拨 {offset}ms（超过 {MaxClockBackwardMs}ms 阈值），拒绝生成雪花 ID。");
                }
            }

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & SequenceMask;
                if (_sequence == 0)
                {
                    timestamp = WaitNextMillis(_lastTimestamp);
                }
            }
            else
            {
                _sequence = 0L;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - Epoch) << TimestampLeftShift)
                 | (_datacenterId << DatacenterIdShift)
                 | (_workerId << WorkerIdShift)
                 | _sequence;
        }
    }

    private static long CurrentTimeMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long WaitNextMillis(long lastTimestamp)
    {
        var timestamp = CurrentTimeMillis();
        while (timestamp <= lastTimestamp)
        {
            timestamp = CurrentTimeMillis();
        }
        return timestamp;
    }
}
