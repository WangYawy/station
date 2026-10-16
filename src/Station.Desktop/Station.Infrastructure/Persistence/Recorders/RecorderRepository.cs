using System.Threading.Tasks;
using Station.Data.Repositories;
using Station.Domain.Recorders;

namespace Station.Infrastructure.Persistence.Recorders;

public sealed class RecorderRepository : IRecorderRepository
{
    private readonly IUnitOfWork _uow;

    public RecorderRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<Recorder?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : RecorderMapper.ToDomain(po);
    }

    public async Task<Recorder?> GetBySerialAsync(string serialNumber, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        var po = await repo.FirstAsync(x => x.SerialNumber == serialNumber, ct);
        return po is null ? null : RecorderMapper.ToDomain(po);
    }
    public async Task<Recorder?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        var po = await repo.FirstAsync(x => x.Code == code, ct);
        return po is null ? null : RecorderMapper.ToDomain(po);
    }

    public async Task<Recorder?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        var po = await repo.FirstAsync(x => x.Name == name, ct);
        return po is null ? null : RecorderMapper.ToDomain(po);
    }
    public Task<bool> ExistsSerialAsync(string serialNumber, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        return repo.IsAnyAsync(x => x.SerialNumber == serialNumber, ct);
    }

    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        return repo.IsAnyAsync(x => x.Code == code, ct);
    }

    public async Task<IReadOnlyList<Recorder>> GetActiveListAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        var list = await repo.GetListAsync(x => x.IsActive, ct);
        return list.Select(RecorderMapper.ToDomain).ToList();
    }

    public Task AddAsync(Recorder recorder, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        return repo.InsertAsync(RecorderMapper.ToPo(recorder), ct);
    }

    public Task UpdateAsync(Recorder recorder, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RecorderPo>();
        return repo.UpdateAsync(RecorderMapper.ToPo(recorder), ct);
    }
}
