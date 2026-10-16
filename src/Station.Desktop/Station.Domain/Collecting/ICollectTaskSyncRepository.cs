namespace Station.Domain.Collecting;
public interface ICollectTaskSyncRepository
{
    CollectTask? GetById(long id);
    IReadOnlyList<CollectTask> GetActive();
    void Update(CollectTask task);
    void Insert(CollectTask task);

    /// <summary>
    /// 只更新状态字段（不碰进度累加字段）。
    /// 用于主线程状态机转换。
    /// </summary>
    void UpdateStateOnly(CollectTask task);

    /// <summary>
    /// 原子累加进度字段并返回最新快照。
    /// UPDATE ... SET CollectedFiles = CollectedFiles + @df, ...  WHERE Id = @id
    /// 返回 null 表示任务不存在或已被删除。
    /// </summary>
    TaskProgressSnapshot? IncrementProgress(
        long taskId,
        int deltaFiles,
        long deltaBytes,
        int deltaFailed,
        double speedBytesPerSecond);
}
