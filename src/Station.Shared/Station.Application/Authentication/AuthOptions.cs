namespace Station.Application.Authentication;

/// <summary>
/// 登录策略配置，对应配置节 <c>Station:Auth</c>。
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Station:Auth";

    /// <summary>连续失败锁定阈值（需求：连续失败 5 次锁定）。</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>锁定分钟数（需求：锁定 15 分钟）。</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>必须登录模式下无操作自动退出分钟数（需求：1 分钟）。</summary>
    public int AutoLogoutMinutes { get; set; } = 1;
}
