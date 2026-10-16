using SqlSugar;

namespace Station.Infrastructure.Persistence.Departments;

[SugarTable("station_dept")]
[SugarIndex("uk_dept_code", nameof(Code), OrderByType.Asc, true)]
public sealed class DeptPo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public long? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? CreatedBy { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? UpdatedBy { get; set; }
}
