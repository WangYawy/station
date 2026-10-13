using Station.Application.Authentication.Commands;
using Station.Domain.Accounts;

namespace Station.Application.Authentication;

/// <summary>
/// 认证服务：登录、登出、自助改密。
/// 这是唯一一个"读账户 + 写账户"混合的用例服务，因其本质是会话建立，不属于聚合 CRUD。
/// </summary>
public interface IAuthenticationService
{
    /// <summary>登录。成功时返回会话，失败时返回原因与锁定截止时间。</summary>
    Task<LoginResult> LoginAsync(LoginCommand command, string sourceClient, string sourceIp, CancellationToken ct = default);

    /// <summary>登出（仅记审计）。</summary>
    Task LogoutAsync(long accountId, string sourceClient, string sourceIp, CancellationToken ct = default);

    /// <summary>用户自助修改密码，需校验旧密码。</summary>
    Task<ChangePasswordResult> ChangePasswordAsync(
        ChangePasswordCommand command, string sourceClient, string sourceIp, CancellationToken ct = default);

    /// <summary>供其他模块调用的密码校验入口（内含惰性重哈希）。</summary>
    Task<bool> VerifyAsync(Account user, string password, CancellationToken ct = default);
}
