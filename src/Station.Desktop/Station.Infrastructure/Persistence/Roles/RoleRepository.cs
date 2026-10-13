using Station.Data.Repositories;
using Station.Domain.Roles;

namespace Station.Infrastructure.Persistence.Roles;

public sealed class RoleRepository : IRoleRepository
{
    private readonly IUnitOfWork _uow;

    public RoleRepository(IUnitOfWork uow) => _uow = uow;

    public async Task<Role?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : RoleMapper.ToDomain(po);
    }

    public async Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        var po = await repo.FirstAsync(x => x.Code == code, ct);
        return po is null ? null : RoleMapper.ToDomain(po);
    }

    public Task<bool> ExistsCodeAsync(string code, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        return repo.IsAnyAsync(x => x.Code == code, ct);
    }

    public async Task<IReadOnlyList<Role>> GetListAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        var list = await repo.GetListAsync(null, ct);
        return list.Select(RoleMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Role>> GetActiveListAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        var list = await repo.GetListAsync(x => x.IsActive, ct);
        return list.Select(RoleMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Role>> GetActiveByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<Role>();
        var repo = _uow.GetRepository<RolePo>();
        var list = await repo.GetListAsync(x => ids.Contains(x.Id) && x.IsActive, ct);
        return list.Select(RoleMapper.ToDomain).ToList();
    }

    public Task AddAsync(Role role, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        return repo.InsertAsync(RoleMapper.ToPo(role), ct);
    }

    public Task UpdateAsync(Role role, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        return repo.UpdateAsync(RoleMapper.ToPo(role), ct);
    }

    public Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePo>();
        return repo.DeleteByIdAsync(id, ct);
    }
}
