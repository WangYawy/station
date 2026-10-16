using SqlSugar;
using Station.Data.SqlSugar;
using Station.Domain.Outbox;

namespace Station.Infrastructure.Persistence.Outbox;

public sealed class SyncOutboxSyncRepository : ISyncOutboxSyncRepository
{
    private readonly ILoopSqlSugarClient _db;
    public SyncOutboxSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public IReadOnlyList<SyncOutboxItem> TakePending(int maxCount)
    {
        if (maxCount <= 0) return Array.Empty<SyncOutboxItem>();

        var now = DateTime.Now;
        var list = _db.Queryable<SyncOutboxPo>()
            .Where(x => x.Status == (int)SyncOutboxStatus.Pending
                     && (x.NextRetryAt == null || x.NextRetryAt <= now))
            .OrderBy(x => x.Id)
            .Take(maxCount)
            .ToList();

        return list.Select(SyncOutboxMapper.ToDomain).ToList();
    }

    public void Update(SyncOutboxItem item)
        => _db.Updateable(SyncOutboxMapper.ToPo(item)).ExecuteCommand();

    public void UpdateRange(IEnumerable<SyncOutboxItem> items)
        => _db.Updateable(items.Select(SyncOutboxMapper.ToPo).ToList()).ExecuteCommand();

    public void Insert(SyncOutboxItem item)
        => _db.Insertable(SyncOutboxMapper.ToPo(item)).ExecuteCommand();

    public int CountPending()
    {
        var now = DateTime.Now;
        return _db.Queryable<SyncOutboxPo>()
            .Where(x => x.Status == (int)SyncOutboxStatus.Pending
                     && (x.NextRetryAt == null || x.NextRetryAt <= now))
            .Count();
    }
}
