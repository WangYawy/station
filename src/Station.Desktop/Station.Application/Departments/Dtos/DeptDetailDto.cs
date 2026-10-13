namespace Station.Application.Departments.Dtos;

public sealed class DeptDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? ParentId { get; set; }
    public string? ParentName { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int ChildCount { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
