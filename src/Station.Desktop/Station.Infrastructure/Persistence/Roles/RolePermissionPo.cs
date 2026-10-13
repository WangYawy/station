using SqlSugar;

namespace Station.Infrastructure.Persistence.Roles;

[SugarTable("station_role_permission")]
[SugarIndex("uk_role_perm", nameof(RoleId), OrderByType.Asc, nameof(PermissionId), OrderByType.Asc, true)]
public sealed class RolePermissionPo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long RoleId { get; set; }
    public long PermissionId { get; set; }
}
