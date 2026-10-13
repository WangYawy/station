namespace Station.Application.Departments.Commands;

/// <summary>创建部门命令。</summary>
public sealed class CreateDeptCommand
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long? ParentId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}
