using Station.Data.SqlSugar;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

public sealed class CollectTaskSyncRepository : ICollectTaskSyncRepository
{
    private readonly ILoopSqlSugarClient _db;

    public CollectTaskSyncRepository(ILoopSqlSugarClient db) => _db = db;

    public CollectTask? GetById(long id)
    {
        var po = _db.Queryable<CollectTaskPo>().In(id).First();
        return po is null ? null : CollectTaskMapper.ToDomain(po);
    }

    public IReadOnlyList<CollectTask> GetActive()
    {
        var list = _db.Queryable<CollectTaskPo>()
            .Where(x => x.Status == CollectTaskStatus.Scanning
                     || x.Status == CollectTaskStatus.Collecting
                     || x.Status == CollectTaskStatus.Paused)
            .ToList();
        return list.Select(CollectTaskMapper.ToDomain).ToList();
    }

    public void Insert(CollectTask task)
        => _db.Insertable(CollectTaskMapper.ToPo(task)).ExecuteCommand();

    /// <summary>
    /// 整行更新。用于任务终态或初始化时一次性写入。
    /// 会覆盖进度字段（终态时进度已定，无需 Aggregator 累加）。
    /// </summary>
    public void Update(CollectTask task)
        => _db.Updateable(CollectTaskMapper.ToPo(task)).ExecuteCommand();

    /// <summary>
    /// 只写状态字段，不碰进度
    /// </summary>
    /// <param name="task"></param>
    public void UpdateStateOnly(CollectTask task)
    {
        var po = CollectTaskMapper.ToPo(task);

        _db.Updateable(po)
            .UpdateColumns(x => new
            {
                x.Status,
                x.ErrorMessage,
                x.StartedAt,
                x.CompletedAt,
                x.IsEmergency,
                x.SyncStatus,
                x.UploadError,
                x.LedgeredAt,
                x.TotalFiles,
                x.TotalBytes,
                x.SkippedFiles,
                x.UploadedFiles,
                x.UploadedBytes
            })
            .ExecuteCommand();
    }

    /// <summary>
    /// 原子累加进度 + 返回最新快照
    /// </summary>
    public TaskProgressSnapshot? IncrementProgress(
        long taskId,
        int deltaFiles,
        long deltaBytes,
        int deltaFailed,
        double speedBytesPerSecond)
    {
        // 1. 原子累加（字段 = 字段 + delta），避免读改写竞争
        var affected = _db.Ado.ExecuteCommand(
            @"UPDATE station_collect_task
              SET CollectedFiles = CollectedFiles + @df,
                  CollectedBytes = CollectedBytes + @db,
                  FailedFiles    = FailedFiles + @dfa,
                  SpeedBytesPerSecond = @sp
              WHERE Id = @id",
            new
            {
                id = taskId,
                df = deltaFiles,
                db = deltaBytes,
                dfa = deltaFailed,
                sp = speedBytesPerSecond
            });

        if (affected == 0) return null;

        // 2. 取回最新快照
        var snapshot = _db.Queryable<CollectTaskPo>()
            .Where(x => x.Id == taskId)
            .Select(x => new TaskProgressSnapshot(
                x.Id,
                x.TotalFiles,
                x.CollectedFiles,
                x.FailedFiles,
                x.TotalBytes,
                x.CollectedBytes,
                x.SpeedBytesPerSecond))
            .First();

        return snapshot;
    }

}
