using Station.Data.Repositories;
using Station.Domain.Outbox;

namespace Station.Infrastructure.Persistence.Outbox;

public sealed class SyncOutboxRepository : ISyncOutboxRepository
{
    private readonly IUnitOfWork _uow;
    public SyncOutboxRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<SyncOutboxItem?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<SyncOutboxPo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : SyncOutboxMapper.ToDomain(po);
    }

    public Task AddAsync(SyncOutboxItem item, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<SyncOutboxPo>();
        return repo.InsertAsync(SyncOutboxMapper.ToPo(item), ct);
    }

    public Task UpdateAsync(SyncOutboxItem item, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<SyncOutboxPo>();
        return repo.UpdateAsync(SyncOutboxMapper.ToPo(item), ct);
    }
}
