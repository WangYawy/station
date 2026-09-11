using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 权限点（功能权限）：编码形如 module:action，如 file:view / user:manage。
/// 预置权限目录随版本种子写入，管理员可维护。
/// </summary>
[SugarTable("station_permission")]
[SugarIndex("uk_perm_code", nameof(Code), OrderByType.Asc, true)]
public sealed class Permission
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string Code { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string Module { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
