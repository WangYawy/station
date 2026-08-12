namespace Station.Domain.Enums;

/// <summary>
/// 角色数据范围：决定"本部门数据"的可见边界。
/// All=全部数据；DeptAndChildren=本部门及下级；Dept=仅本部门；Self=仅本人。
/// </summary>
public enum DataScope
{
    All = 0,
    DeptAndChildren = 1,
    Dept = 2,
    Self = 3
}
