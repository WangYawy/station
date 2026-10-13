using SqlSugar;

namespace Station.Infrastructure.Persistence.Permissions;

[SugarTable("station_permission")]
[SugarIndex("uk_perm_code", nameof(Code), OrderByType.Asc, true)]
public sealed class PermissionPo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string Module { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}
