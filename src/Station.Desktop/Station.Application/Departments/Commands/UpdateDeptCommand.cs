namespace Station.Application.Departments.Commands;

/// <summary>
/// 更新部门命令。
/// 注意：Code 是跨端自然键，创建后不可变，因此不在更新命令中。
/// </summary>
public sealed class UpdateDeptCommand
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public long? ParentId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}
