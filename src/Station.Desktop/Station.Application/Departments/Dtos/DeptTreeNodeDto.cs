namespace Station.Application.Departments.Dtos;

public sealed class DeptTreeNodeDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public List<DeptTreeNodeDto> Children { get; set; } = [];
}
