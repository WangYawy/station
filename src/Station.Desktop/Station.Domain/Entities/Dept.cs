using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>部门（组织节点）。部门编码为跨端自然键；单机版轻量列表，平台版多级组织树。</summary>
[SugarTable("station_dept")]
[SugarIndex("uk_dept_code", nameof(Code), OrderByType.Asc, true)]
public sealed class Dept
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

    public bool IsActive { get; set; } = true;
}
