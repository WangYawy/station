namespace Station.Application.Roles.Dtos;

public sealed class RoleListItemDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DataScope { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public int PermissionCount { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
