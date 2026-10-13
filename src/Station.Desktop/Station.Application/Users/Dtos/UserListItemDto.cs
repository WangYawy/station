namespace Station.Application.Users.Dtos;

public sealed class UserListItemDto
{
    public long Id { get; set; }
    public string UserNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long DeptId { get; set; }
    public string? DeptName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>关联账号数（同一人员可挂多个账号）。</summary>
    public int AccountCount { get; set; }
}
