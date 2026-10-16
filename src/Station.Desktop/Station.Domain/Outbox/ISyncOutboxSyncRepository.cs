namespace Station.Domain.Outbox;

/// <summary>长连接、后台 Worker 批量取用。</summary>
public interface ISyncOutboxSyncRepository
{
    IReadOnlyList<SyncOutboxItem> TakePending(int maxCount);
    void Update(SyncOutboxItem item);
    void UpdateRange(IEnumerable<SyncOutboxItem> items);
    void Insert(SyncOutboxItem item);
    int CountPending();
}
