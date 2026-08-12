namespace Station.Domain.Entities;

/// <summary>
/// 账号：登录凭据与 RBAC 主体；可选关联一个用户（人员），
/// 用于确定"本人数据"的数据范围。
/// </summary>
public sealed class Account
{
    public long Id { get; set; }

    public required string UserName { get; set; }

    public required string PasswordHash { get; set; }

    /// <summary>关联的用户（人员）ID，可空。</summary>
    public long? UserId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }
}
