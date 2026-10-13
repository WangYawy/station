
namespace Station.Domain.Accounts;

/// <summary>登录失败后的领域结果。</summary>
public enum LoginFailureOutcome
{
    /// <summary>凭据无效（密码错误、账号不存在等）。</summary>
    InvalidCredentials,

    /// <summary>账号已因失败次数达到上限被锁定。</summary>
    LockedOut
}
