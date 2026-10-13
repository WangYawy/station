namespace Station.Domain.Roles;

public interface IRolePermissionRepository
{
    Task<IReadOnlyList<long>> GetPermissionIdsByRoleIdAsync(long roleId, CancellationToken ct = default);
    Task<IReadOnlyList<long>> GetPermissionIdsByRoleIdsAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct = default);
    Task<IReadOnlyList<RolePermission>> GetByRoleIdAsync(long roleId, CancellationToken ct = default);

    Task DeleteByRoleIdAsync(long roleId, CancellationToken ct = default);
    Task DeleteByPermissionIdAsync(long permissionId, CancellationToken ct = default);

    Task AddRangeAsync(long roleId, IReadOnlyList<long> permissionIds, CancellationToken ct = default);
}
