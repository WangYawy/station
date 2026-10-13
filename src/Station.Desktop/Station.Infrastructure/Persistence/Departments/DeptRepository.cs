using Station.Data.Repositories;
using Station.Domain.Departments;

namespace Station.Infrastructure.Persistence.Departments;

public sealed class DeptRepository : IDeptRepository
{
    private readonly IUnitOfWork _uow;

    public DeptRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<Dept?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : DeptMapper.ToDomain(po);
    }

    public async Task<Dept?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        var po = await repo.FirstAsync(x => x.Code == code, ct);
        return po is null ? null : DeptMapper.ToDomain(po);
    }

    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        return repo.IsAnyAsync(x => x.Code == code, ct);
    }

    public Task<bool> HasChildrenAsync(long parentId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        return repo.IsAnyAsync(x => x.ParentId == parentId, ct);
    }

    public async Task<IReadOnlyList<Dept>> GetActiveListAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        var list = await repo.GetListAsync(x => x.IsActive, ct);
        return list.Select(DeptMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Dept>> GetActiveChildrenAsync(long parentId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        var list = await repo.GetListAsync(x => x.ParentId == parentId && x.IsActive, ct);
        return list.Select(DeptMapper.ToDomain).ToList();
    }

    public Task AddAsync(Dept dept, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        return repo.InsertAsync(DeptMapper.ToPo(dept), ct);
    }

    public Task UpdateAsync(Dept dept, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        return repo.UpdateAsync(DeptMapper.ToPo(dept), ct);
    }

    public Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<DeptPo>();
        return repo.DeleteByIdAsync(id, ct);
    }
}
