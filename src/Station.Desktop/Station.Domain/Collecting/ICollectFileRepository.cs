namespace Station.Domain.Collecting;

public interface ICollectFileRepository
{
    Task<CollectFile?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<CollectFile?> GetByFileNoAsync(string fileNo, CancellationToken ct = default);
    Task<IReadOnlyList<CollectFile>> GetByTaskIdAsync(long taskId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectFile>> GetPendingByTaskIdAsync(long taskId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectFile>> GetPendingUploadsByTaskIdAsync(long taskId, CancellationToken ct = default);
    Task<HashSet<string>> GetCompletedFingerprintsAsync(IReadOnlyCollection<string> fingerprints, CancellationToken ct = default);
    Task<int> CountPendingUploadsAsync(CancellationToken ct = default);

    Task AddAsync(CollectFile file, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<CollectFile> files, CancellationToken ct = default);
    Task UpdateAsync(CollectFile file, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<CollectFile> files, CancellationToken ct = default);
}
