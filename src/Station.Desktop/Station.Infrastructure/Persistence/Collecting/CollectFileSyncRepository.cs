using Station.Data.SqlSugar;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

public sealed class CollectFileSyncRepository : ICollectFileSyncRepository
{
    private readonly ILoopSqlSugarClient _db;

    public CollectFileSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public CollectFile? GetById(long id)
    {
        var po = _db.Queryable<CollectFilePo>().In(id).First();
        return po is null ? null : CollectFileMapper.ToDomain(po);
    }

    public IReadOnlyList<CollectFile> GetPendingByTaskId(long taskId)
    {
        var list = _db.Queryable<CollectFilePo>()
            .Where(x => x.TaskId == taskId && x.Status == CollectFileStatus.Pending)
            .OrderBy(x => x.Id)
            .ToList();
        return list.Select(CollectFileMapper.ToDomain).ToList();
    }

    public IReadOnlyList<CollectFile> GetByTaskId(long taskId)
    {
        var list = _db.Queryable<CollectFilePo>()
            .Where(x => x.TaskId == taskId)
            .ToList();
        return list.Select(CollectFileMapper.ToDomain).ToList();
    }

    public IReadOnlyList<CollectFile> GetPendingUploadsByTaskId(long taskId)
    {
        var list = _db.Queryable<CollectFilePo>()
            .Where(x => x.TaskId == taskId && x.SyncStatus == UploadStatus.Pending)
            .OrderBy(x => x.Id)
            .ToList();
        return list.Select(CollectFileMapper.ToDomain).ToList();
    }

    public int CountPendingUploadsByTaskId(long taskId)
        => _db.Queryable<CollectFilePo>()
            .Where(x => x.TaskId == taskId && x.SyncStatus == UploadStatus.Pending)
            .Count();

    public int CountPendingUploads()
        => _db.Queryable<CollectFilePo>()
            .Where(x => x.SyncStatus == UploadStatus.Pending)
            .Count();

    public void InsertRange(IEnumerable<CollectFile> files)
        => _db.Insertable(files.Select(CollectFileMapper.ToPo).ToList()).ExecuteCommand();

    public void Update(CollectFile file)
        => _db.Updateable(CollectFileMapper.ToPo(file)).ExecuteCommand();

    public void UpdateRange(IEnumerable<CollectFile> files)
        => _db.Updateable(files.Select(CollectFileMapper.ToPo).ToList()).ExecuteCommand();
}
