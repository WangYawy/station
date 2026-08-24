using Microsoft.Extensions.Options;
using Station.Domain.Entities;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Application.Audit;
using Station.Application.Authorization;

namespace Station.Application.Authentication;
using Microsoft.Extensions.Logging;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IRepository<Account> _accounts;
    private readonly IAuthorizationService _authorization;
    private readonly IAuditLogService _audit;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IRepository<Account> accounts,
        IAuthorizationService authorization,
        IAuditLogService audit,
        IPasswordHasher passwordHasher,
        IOptions<AuthOptions> options,
        ILogger<AuthenticationService> logger)
    {
        _accounts = accounts;
        _authorization = authorization;
        _audit = audit;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        var account = await _accounts.FirstAsync(a => a.UserName == request.UserName);
        if (account is null)
        {
            _logger.LogWarning("登录失败：账号 {User} 不存在（IP {Ip}）", request.UserName, request.SourceIp);
            await _audit.WriteAsync(new AuditLog
            {
                OperatorAccount = request.UserName,
                SourceIp = request.SourceIp,
                OperationType = "login",
                Target = request.UserName,
                Detail = "登录失败：账号不存在",
                Result = 0,
                CreatedAt = DateTime.Now
            });
            return new LoginResult(false, LoginFailureReason.InvalidCredentials, null);
        }

        if (!account.IsEnabled)
        {
            await WriteLoginAudit(account, request.SourceIp, "登录失败：账号已禁用");
            return new LoginResult(false, LoginFailureReason.Disabled, null);
        }

        if (account.LockedUntil is { } lockedUntil && lockedUntil > DateTime.Now)
        {
            _logger.LogWarning("登录失败：账号 {User} 锁定至 {Until}", request.UserName, lockedUntil);
            await WriteLoginAudit(account, request.SourceIp, $"登录失败：账号锁定至 {lockedUntil:HH:mm:ss}");
            return new LoginResult(false, LoginFailureReason.LockedOut, null, lockedUntil);
        }

        if (!_passwordHasher.Verify(request.Password, account.PasswordHash))
        {
            account.FailedLoginAttempts++;
            DateTime? newLockUntil = null;
            if (account.FailedLoginAttempts >= _options.MaxFailedAttempts)
            {
                newLockUntil = DateTime.Now.AddMinutes(_options.LockoutMinutes);
                account.LockedUntil = newLockUntil;
                account.FailedLoginAttempts = 0;
            }

            await _accounts.UpdateAsync(account);
            await WriteLoginAudit(account, request.SourceIp,
                newLockUntil is null
                    ? $"登录失败：密码错误（第 {account.FailedLoginAttempts + 1} 次）"
                    : $"登录失败：密码错误，账号锁定 {_options.LockoutMinutes} 分钟");
            _logger.LogWarning("登录失败：账号 {User} 密码错误（IP {Ip}，第 {Attempt} 次{Locked}）",
                request.UserName, request.SourceIp, account.FailedLoginAttempts,
                newLockUntil is null ? string.Empty : "，已锁定");
            return new LoginResult(
                false,
                newLockUntil is null ? LoginFailureReason.InvalidCredentials : LoginFailureReason.LockedOut,
                null,
                newLockUntil);
        }

        account.FailedLoginAttempts = 0;
        account.LockedUntil = null;
        await _accounts.UpdateAsync(account);

        var session = await _authorization.GetSessionAsync(account.Id);
        await WriteLoginAudit(account, request.SourceIp, "登录成功");
        _logger.LogInformation("登录成功：{User}（IP {Ip}）", request.UserName, request.SourceIp);
        return new LoginResult(true, null, session);
    }

    public async Task LogoutAsync(long accountId, string? sourceIp = null)
    {
        var account = await _accounts.GetByIdAsync(accountId);
        if (account is not null)
        {
            await WriteLoginAudit(account, sourceIp, "退出登录");
        }
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(long accountId, string oldPassword, string newPassword)
    {
        var account = await _accounts.GetByIdAsync(accountId);
        if (account is null)
        {
            return new ChangePasswordResult(false, "账号不存在");
        }

        if (!_passwordHasher.Verify(oldPassword, account.PasswordHash))
        {
            await WriteLoginAudit(account, null, "修改密码失败：旧密码错误");
            return new ChangePasswordResult(false, "旧密码错误");
        }

        account.PasswordHash = _passwordHasher.Hash(newPassword);
        await _accounts.UpdateAsync(account);
        await WriteLoginAudit(account, null, "修改密码成功");
        return new ChangePasswordResult(true);
    }

    private async Task WriteLoginAudit(Account account, string? sourceIp, string detail)
    {
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = account.UserName,
            OperatorUserId = account.UserId,
            SourceIp = sourceIp,
            OperationType = "login",
            Target = account.UserName,
            Detail = detail,
            Result = detail.Contains("成功") ? 1 : 0,
            CreatedAt = DateTime.Now
        });
    }
}
