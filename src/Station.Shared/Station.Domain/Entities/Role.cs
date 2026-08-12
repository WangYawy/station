using SqlSugar;
using Station.Domain.Enums;

namespace Station.Domain.Entities;

/// <summary>
/// 角色：预置 管理员/部门负责人/操作员/审计员 + 管理员自定义角色。
/// DataScope 决定该角色的数据可见范围。
/// </summary>
[SugarTable("station_role")]
[SugarIndex("uk_role_code", nameof(Code), OrderByType.Asc, true)]
public sealed class Role
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 32)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    public DataScope DataScope { get; set; } = DataScope.Self;

    /// <summary>预置角色不可删除、编码不可改。</summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;
}
