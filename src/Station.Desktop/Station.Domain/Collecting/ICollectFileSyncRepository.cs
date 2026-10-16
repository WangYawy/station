namespace Station.Domain.Collecting;

public interface ICollectFileSyncRepository
{
    CollectFile? GetById(long id);
    IReadOnlyList<CollectFile> GetPendingByTaskId(long taskId);
    IReadOnlyList<CollectFile> GetByTaskId(long taskId);
    /// <summary>按任务查待上传文件（按 Id 升序）。</summary>
    IReadOnlyList<CollectFile> GetPendingUploadsByTaskId(long taskId);

    /// <summary>任务内待上传文件数。</summary>
    int CountPendingUploadsByTaskId(long taskId);
    void Update(CollectFile file);
    void UpdateRange(IEnumerable<CollectFile> files);
    void InsertRange(IEnumerable<CollectFile> files);
    int CountPendingUploads();
}
