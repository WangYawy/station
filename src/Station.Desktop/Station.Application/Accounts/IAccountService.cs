using Station.Application.Accounts.Commands;
using Station.Application.Accounts.Dtos;
using Station.Application.Users;

namespace Station.Application.Accounts;

/// <summary>
/// 账号应用服务（写侧）。
/// 读侧请使用 <c>IAccountQueryService</c>。
/// 登录、登出、自助改密请使用 <c>IAuthenticationService</c>。
/// </summary>
public interface IAccountService
{
    /// <summary>为指定用户创建登录账号。</summary>
    Task<CreateAccountResult> CreateAsync(
        CreateAccountCommand command, CancellationToken ct = default);

    /// <summary>管理员重置账号密码，并清除失败计数与锁定状态。</summary>
    Task<OpResult> ResetPasswordAsync(
        ResetPasswordCommand command, CancellationToken ct = default);
}
