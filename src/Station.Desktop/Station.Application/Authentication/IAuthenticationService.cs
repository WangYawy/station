namespace Station.Application.Authentication;

/// <summary>登录认证服务：登录/登出/改密，含失败锁定策略。</summary>
public interface IAuthenticationService
{
    Task<LoginResult> LoginAsync(LoginRequest request);

    Task LogoutAsync(long accountId, string? sourceIp = null);

    Task<ChangePasswordResult> ChangePasswordAsync(long accountId, string oldPassword, string newPassword);
}
