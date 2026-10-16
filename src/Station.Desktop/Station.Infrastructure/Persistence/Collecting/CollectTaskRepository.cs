using Station.Data.Repositories;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

public sealed class CollectTaskRepository : ICollectTaskRepository
{
    private readonly IUnitOfWork _uow;

    public CollectTaskRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<CollectTask?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectTaskPo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : CollectTaskMapper.ToDomain(po);
    }

    public async Task<IReadOnlyList<CollectTask>> GetActiveAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectTaskPo>();
        var list = await repo.GetListAsync(x =>
            x.Status == CollectTaskStatus.Scanning ||
            x.Status == CollectTaskStatus.Collecting ||
            x.Status == CollectTaskStatus.Paused, ct);
        return list.Select(CollectTaskMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<CollectTask>> GetRecentAsync(
        int count, CancellationToken ct = default)
    {
        if (count <= 0) return Array.Empty<CollectTask>();
        var repo = _uow.GetRepository<CollectTaskPo>();
        var list = await repo.GetListAsync(null, ct);
        return list
            .OrderByDescending(t => t.CreatedAt)
            .Take(count)
            .Select(CollectTaskMapper.ToDomain)
            .ToList();
    }

    public Task<int> CountActiveEmergencyAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectTaskPo>();
        return repo.CountAsync(x => x.IsEmergency && (
            x.Status == CollectTaskStatus.Scanning ||
            x.Status == CollectTaskStatus.Collecting ||
            x.Status == CollectTaskStatus.Paused), ct);
    }

    public Task AddAsync(CollectTask task, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectTaskPo>();
        return repo.InsertAsync(CollectTaskMapper.ToPo(task), ct);
    }

    public Task UpdateAsync(CollectTask task, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<CollectTaskPo>();
        return repo.UpdateAsync(CollectTaskMapper.ToPo(task), ct);
    }
}
