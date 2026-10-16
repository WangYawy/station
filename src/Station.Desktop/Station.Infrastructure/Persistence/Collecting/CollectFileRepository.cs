using Station.Data.Repositories;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

public sealed class CollectFileRepository : ICollectFileRepository
{
    private readonly IUnitOfWork _uow;

    public CollectFileRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<CollectFile?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : CollectFileMapper.ToDomain(po);
    }
    public async Task<CollectFile?> GetByFileNoAsync(string fileNo, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        var po = await repo.FirstAsync(x => x.FileNo == fileNo, ct);
        return po is null ? null : CollectFileMapper.ToDomain(po);
    }
    public async Task<IReadOnlyList<CollectFile>> GetByTaskIdAsync(
        long taskId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        var list = await repo.GetListAsync(x => x.TaskId == taskId, ct);
        return list.Select(CollectFileMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<CollectFile>> GetPendingByTaskIdAsync(
        long taskId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        var list = await repo.GetListAsync(x =>
            x.TaskId == taskId && x.Status == CollectFileStatus.Pending, ct);
        return list.OrderBy(f => f.Id).Select(CollectFileMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<CollectFile>> GetPendingUploadsByTaskIdAsync(
        long taskId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        var list = await repo.GetListAsync(x =>
            x.TaskId == taskId && x.SyncStatus == UploadStatus.Pending, ct);
        return list.Select(CollectFileMapper.ToDomain).ToList();
    }

    public async Task<HashSet<string>> GetCompletedFingerprintsAsync(
        IReadOnlyCollection<string> fingerprints, CancellationToken ct = default)
    {
        if (fingerprints.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

        var repo = _uow.GetRepository<CollectFilePo>();
        var list = await repo.GetListAsync(x =>
            fingerprints.Contains(x.Fingerprint!) &&
            x.Status == CollectFileStatus.Completed, ct);

        return new HashSet<string>(
            list.Select(x => x.Fingerprint!).Where(fp => !string.IsNullOrEmpty(fp)),
            StringComparer.Ordinal);
    }

    public Task<int> CountPendingUploadsAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        return repo.CountAsync(x => x.SyncStatus == UploadStatus.Pending, ct);
    }

    public Task AddAsync(CollectFile file, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        return repo.InsertAsync(CollectFileMapper.ToPo(file), ct);
    }

    public Task AddRangeAsync(IEnumerable<CollectFile> files, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        return repo.InsertRangeAsync(files.Select(CollectFileMapper.ToPo).ToList(), ct);
    }

    public Task UpdateAsync(CollectFile file, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        return repo.UpdateAsync(CollectFileMapper.ToPo(file), ct);
    }

    public Task UpdateRangeAsync(IEnumerable<CollectFile> files, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectFilePo>();
        return repo.UpdateRangeAsync(files.Select(CollectFileMapper.ToPo).ToList(), ct);
    }
}
