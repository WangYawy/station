using Station.Data.IdGeneration;
using Station.Data.Repositories;
using Station.Domain.Roles;

namespace Station.Infrastructure.Persistence.Roles;

public sealed class RolePermissionRepository : IRolePermissionRepository
{
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;

    public RolePermissionRepository(IUnitOfWork uow, IIdGenerator ids)
    {
        _uow = uow;
        _ids = ids;
    }

    public async Task<IReadOnlyList<long>> GetPermissionIdsByRoleIdAsync(
        long roleId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePermissionPo>();
        var list = await repo.GetListAsync(x => x.RoleId == roleId, ct);
        return list.Select(x => x.PermissionId).ToList();
    }

    public async Task<IReadOnlyList<long>> GetPermissionIdsByRoleIdsAsync(
        IReadOnlyCollection<long> roleIds, CancellationToken ct = default)
    {
        if (roleIds.Count == 0) return Array.Empty<long>();
        var repo = _uow.GetRepository<RolePermissionPo>();
        var list = await repo.GetListAsync(x => roleIds.Contains(x.RoleId), ct);
        return list.Select(x => x.PermissionId).Distinct().ToList();
    }

    public async Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(
        long roleId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePermissionPo>();
        var list = await repo.GetListAsync(x => x.RoleId == roleId, ct);
        return list.Select(x => RolePermission.Create(x.Id, x.RoleId, x.PermissionId)).ToList();
    }

    public Task DeleteByRoleIdAsync(long roleId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePermissionPo>();
        return repo.DeleteAsync(x => x.RoleId == roleId, ct);
    }

    public Task DeleteByPermissionIdAsync(long permissionId, CancellationToken ct = default)
    {
        var repo = _uow.GetRepository<RolePermissionPo>();
        return repo.DeleteAsync(x => x.PermissionId == permissionId, ct);
    }

    public async Task AddRangeAsync(
        long roleId, IReadOnlyList<long> permissionIds, CancellationToken ct = default)
    {
        var distinct = permissionIds.Distinct().ToList();
        if (distinct.Count == 0) return;

        var links = distinct.Select(pid => new RolePermissionPo
        {
            Id = _ids.NewId(),
            RoleId = roleId,
            PermissionId = pid
        }).ToList();

        var repo = _uow.GetRepository<RolePermissionPo>();
        await repo.InsertRangeAsync(links, ct);
    }
}
