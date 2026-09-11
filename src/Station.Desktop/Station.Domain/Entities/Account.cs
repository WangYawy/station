using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 账号：登录凭证与 RBAC 主体；可选关联一个用户（人员）。
/// 桌面端、单机 Web、平台共用同一套账号体系。
/// </summary>
[SugarTable("station_account")]
[SugarIndex("uk_account_username", nameof(UserName), OrderByType.Asc, true)]
public sealed class Account
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>密码哈希：sm3$迭代次数$盐$哈希（PBKDF2-HMAC-SM3）。</summary>
    [SugarColumn(Length = 256)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>关联的用户（人员）ID，可空（如纯管理账号）。</summary>
    [SugarColumn(IsNullable = true)]
    public long? UserId { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int FailedLoginAttempts { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LockedUntil { get; set; }
}
