namespace Station.Application.Accounts.Dtos;

public sealed class AccountDetailDto
{
    public long Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public long? UserId { get; set; }
    public bool IsEnabled { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
