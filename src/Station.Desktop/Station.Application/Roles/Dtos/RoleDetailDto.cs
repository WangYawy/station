namespace Station.Application.Roles.Dtos;

public sealed class RoleDetailDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DataScope { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<long> PermissionIds { get; set; } = [];
    public List<RolePermissionBriefDto> Permissions { get; set; } = [];
}

public sealed class RolePermissionBriefDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
}
