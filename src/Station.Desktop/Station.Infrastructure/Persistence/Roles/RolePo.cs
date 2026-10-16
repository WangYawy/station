using SqlSugar;

namespace Station.Infrastructure.Persistence.Roles;

[SugarTable("station_role")]
[SugarIndex("uk_role_code", nameof(Code), OrderByType.Asc, true)]
public sealed class RolePo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>DataScope 枚举的 int 值。</summary>
    public int DataScope { get; set; }

    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? CreatedBy { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? UpdatedBy { get; set; }
}
