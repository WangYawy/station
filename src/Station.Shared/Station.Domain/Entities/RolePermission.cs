using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>角色-权限点 关联。</summary>
[SugarTable("station_role_permission")]
[SugarIndex("uk_role_perm", nameof(RoleId), OrderByType.Asc, nameof(PermissionId), OrderByType.Asc, true)]
public sealed class RolePermission
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long RoleId { get; set; }

    public long PermissionId { get; set; }
}
