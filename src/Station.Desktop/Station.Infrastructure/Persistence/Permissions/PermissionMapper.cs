using Station.Domain.Permissions;

namespace Station.Infrastructure.Persistence.Permissions;

internal static class PermissionMapper
{
    public static Permission ToDomain(PermissionPo po)
        => Permission.Restore(
            po.Id, po.Code, po.Name, po.Module, po.IsActive,
            po.CreatedAt, po.CreatedBy, po.UpdatedAt, po.UpdatedBy);

    public static PermissionPo ToPo(Permission p)
        => new()
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Module = p.Module,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            CreatedBy = p.CreatedBy,
            UpdatedAt = p.UpdatedAt,
            UpdatedBy = p.UpdatedBy
        };

    public static IEnumerable<PermissionPo> ToPo(IEnumerable<Permission> d)
    {
        return d.Select(x => ToPo(x));
    }
}

