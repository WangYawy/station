namespace Station.Domain.Outbox;

/// <summary>
/// 站→平台上报队列聚合。
/// 状态机：Pending → Sent；失败可重试退避回 Pending
/// </summary>
public sealed class SyncOutboxItem
{
    public long Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public SyncOutboxStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime? NextRetryAt { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? Error { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private SyncOutboxItem() { }

    public static SyncOutboxItem Create(
        long id, string topic, string payloadJson, DateTime now)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic 不能为空。", nameof(topic));

        return new SyncOutboxItem
        {
            Id = id,
            Topic = topic,
            PayloadJson = payloadJson ?? string.Empty,
            Status = SyncOutboxStatus.Pending,
            RetryCount = 0,
            CreatedAt = now
        };
    }

    public static SyncOutboxItem Restore(
        long id, string topic, string payloadJson, SyncOutboxStatus status,
        int retryCount, DateTime? nextRetryAt, DateTime? sentAt,
        string? error, DateTime createdAt)
        => new()
        {
            Id = id,
            Topic = topic,
            PayloadJson = payloadJson,
            Status = status,
            RetryCount = retryCount,
            NextRetryAt = nextRetryAt,
            SentAt = sentAt,
            Error = error,
            CreatedAt = createdAt
        };

    // ---- 行为 ----

    public void MarkSent(DateTime now)
    {
        Status = SyncOutboxStatus.Sent;
        SentAt = now;
        Error = null;
        NextRetryAt = null;
    }

    public void MarkFailed(string error, int maxRetry, DateTime now)
    {
        RetryCount++;
        Error = error;

        if (RetryCount >= maxRetry)
        {
            Status = SyncOutboxStatus.Failed;
            NextRetryAt = null;
        }
        else
        {
            Status = SyncOutboxStatus.Pending;
            NextRetryAt = now.Add(ComputeBackoff(RetryCount));
        }
    }

    public bool IsReadyForSend(DateTime now)
        => Status == SyncOutboxStatus.Pending
           && (NextRetryAt is null || now >= NextRetryAt.Value);

    private static TimeSpan ComputeBackoff(int retryCount)
    {
        // 指数退避：5s、10s、20s、40s… 上限 5 分钟
        var seconds = Math.Min(300, 5 * Math.Pow(2, retryCount - 1));
        return TimeSpan.FromSeconds(seconds);
    }
}
