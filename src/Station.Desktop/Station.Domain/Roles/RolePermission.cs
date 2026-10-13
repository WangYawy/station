namespace Station.Domain.Roles;

/// <summary>角色-权限点关联。无行为，仅作标识。</summary>
public sealed class RolePermission
{
    public long Id { get; private set; }
    public long RoleId { get; private set; }
    public long PermissionId { get; private set; }

    private RolePermission() { }

    public static RolePermission Create(long id, long roleId, long permissionId)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (roleId <= 0) throw new ArgumentOutOfRangeException(nameof(roleId));
        if (permissionId <= 0) throw new ArgumentOutOfRangeException(nameof(permissionId));

        return new RolePermission { Id = id, RoleId = roleId, PermissionId = permissionId };
    }
}
