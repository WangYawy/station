namespace Station.Application.Users.Dtos;

public sealed class UserDetailDto
{
    public long Id { get; set; }
    public string UserNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long DeptId { get; set; }
    public string? DeptName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<UserAccountBriefDto> Accounts { get; set; } = [];
    public List<UserRoleBriefDto> Roles { get; set; } = [];
}

public sealed class UserAccountBriefDto
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public sealed class UserRoleBriefDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DataScope { get; set; }
}
