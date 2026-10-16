using Station.Data.Repositories;
using Station.Domain.Alerts;

namespace Station.Infrastructure.Persistence.Alerts;

public sealed class AlertRepository : IAlertRepository
{
    private readonly IUnitOfWork _uow;

    public AlertRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<Alert?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AlertPo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : AlertMapper.ToDomain(po);
    }

    public Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AlertPo>();
        return repo.CountAsync(x => x.Status == Station.Contracts.AlertStatus.Pending, ct);
    }

    public Task AddAsync(Alert alert, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AlertPo>();
        return repo.InsertAsync(AlertMapper.ToPo(alert), ct);
    }

    public Task UpdateAsync(Alert alert, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<AlertPo>();
        return repo.UpdateAsync(AlertMapper.ToPo(alert), ct);
    }
}
