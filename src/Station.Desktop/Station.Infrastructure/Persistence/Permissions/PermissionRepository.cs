using Station.Data.Repositories;
using Station.Domain.Permissions;

namespace Station.Infrastructure.Persistence.Permissions;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly IUnitOfWork _uow;

    public PermissionRepository(IUnitOfWork uow) => _uow = uow;

    public Task<bool> AnyAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        return repo.IsAnyAsync(_ => true, ct);
    }

    public async Task<Permission?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        var po = await repo.FirstAsync(x => x.Id == id, ct);
        return po is null ? null : PermissionMapper.ToDomain(po);
    }

    public async Task<Permission?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        var po = await repo.FirstAsync(x => x.Code == code, ct);
        return po is null ? null : PermissionMapper.ToDomain(po);
    }

    public async Task<IReadOnlyList<Permission>> GetListAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        var list = await repo.GetListAsync(null, ct);
        return list.Select(PermissionMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Permission>> GetActiveListAsync(CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        var list = await repo.GetListAsync(x => x.IsActive, ct);
        return list.Select(PermissionMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Permission>> GetByCodesAsync(
        IReadOnlyCollection<string> codes, CancellationToken ct = default)
    {
        if (codes.Count == 0) return Array.Empty<Permission>();
        var repo = _uow.GetRepository<PermissionPo>();
        var list = await repo.GetListAsync(x => codes.Contains(x.Code), ct);
        return list.Select(PermissionMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Permission>> GetByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Array.Empty<Permission>();
        var repo = _uow.GetRepository<PermissionPo>();
        var list = await repo.GetListAsync(x => ids.Contains(x.Id), ct);
        return list.Select(PermissionMapper.ToDomain).ToList();
    }

    public Task AddAsync(Permission permission, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        return repo.InsertAsync(PermissionMapper.ToPo(permission), ct);
    }

    public Task AddRangeAsync(IEnumerable<Permission> permissions, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        return repo.InsertRangeAsync(permissions.Select(PermissionMapper.ToPo).ToList(), ct);
    }

    public Task UpdateAsync(Permission permission, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<PermissionPo>();
        return repo.UpdateAsync(PermissionMapper.ToPo(permission), ct);
    }
}
