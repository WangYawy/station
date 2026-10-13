namespace Station.Application.Accounts.Dtos;

public sealed class AccountListItemDto
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public long? UserId { get; set; }
    public string? UserName2 { get; set; }   // 关联用户的姓名（可选展示）
    public bool IsEnabled { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
