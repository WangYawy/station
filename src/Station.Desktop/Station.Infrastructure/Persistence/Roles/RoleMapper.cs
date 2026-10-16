using Station.Domain.Roles;

namespace Station.Infrastructure.Persistence.Roles;

internal static class RoleMapper
{
    public static Role ToDomain(RolePo po)
        => Role.Restore(
            po.Id, po.Code, po.Name, (DataScope)po.DataScope, po.IsSystem, po.IsActive,
            po.CreatedAt, po.CreatedBy, po.UpdatedAt, po.UpdatedBy);

    public static RolePo ToPo(Role r)
        => new()
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            DataScope = (int)r.DataScope,
            IsSystem = r.IsSystem,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt,
            CreatedBy = r.CreatedBy,
            UpdatedAt = r.UpdatedAt,
            UpdatedBy = r.UpdatedBy
        };
}
