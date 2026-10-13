using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Audit;
using Station.Application.Authentication.Commands;
using Station.Application.Authorization;
using Station.Application.Security;
using Station.Domain.Accounts;
using Station.Domain.Entities;

namespace Station.Application.Authentication;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IAccountRepository _accounts;
    private readonly IAuthorizationService _authorization;
    private readonly IAuditLogService _audit;
    private readonly AuthOptions _options;
    private readonly IPasswordService _passwordService;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IAccountRepository accounts,
        IAuthorizationService authorization,
        IAuditLogService audit,
        IOptions<AuthOptions> options,
        IPasswordService passwordService,
        ILogger<AuthenticationService> logger)
    {
        _accounts = accounts;
        _authorization = authorization;
        _audit = audit;
        _options = options.Value;
        _passwordService = passwordService;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(
        LoginCommand command, string sourceClient, string sourceIp, CancellationToken ct = default)
    {
        // ---- 1) 账号存在性 ----
        var account = await _accounts.GetByUserNameAsync(command.UserName, ct);
        if (account is null)
        {
            _logger.LogWarning("登录失败：账号 {User} 不存在（{sourceClient} {Ip}）",
                command.UserName, sourceClient, sourceIp);
            await _audit.WriteAsync(new AuditLog
            {
                OperatorAccount = command.UserName,
                SourceIp = sourceIp,
                OperationType = "login",
                Target = command.UserName,
                Detail = "登录失败：账号不存在",
                SourceClient = "desktop",
                Result = 0,
                CreatedAt = DateTime.Now
            });
            return new LoginResult(false, LoginFailureReason.InvalidCredentials, null);
        }

        // ---- 2) 启用状态 ----
        if (!account.IsEnabled)
        {
            await WriteLoginAudit(account, sourceClient, sourceIp, "登录失败：账号已禁用");
            return new LoginResult(false, LoginFailureReason.Disabled, null);
        }

        // ---- 3) 锁定状态 ----
        var now = DateTime.Now;
        if (account.IsLocked(now))
        {
            _logger.LogWarning("登录失败：账号 {User} 锁定至 {Until}",
                command.UserName, account.LockedUntil);
            await WriteLoginAudit(account, sourceClient, sourceIp,
                $"登录失败：账号锁定至 {account.LockedUntil:HH:mm:ss}");
            return new LoginResult(false, LoginFailureReason.LockedOut, null, account.LockedUntil);
        }

        // ---- 4) 密码校验 ----
        var verify = await _passwordService.VerifyAsync(command.Password, account.PasswordHash, ct);
        if (!verify.Ok)
        {
            var outcome = account.RecordLoginFailure(
                now,
                _options.MaxFailedAttempts,
                TimeSpan.FromMinutes(_options.LockoutMinutes));

            await _accounts.UpdateAsync(account, ct);
            await WriteLoginAudit(account, sourceClient, sourceIp,
                outcome == LoginFailureOutcome.LockedOut
                    ? $"登录失败：密码错误，账号锁定 {_options.LockoutMinutes} 分钟"
                    : $"登录失败：密码错误（第 {account.FailedLoginAttempts} 次）");

            return new LoginResult(
                false,
                outcome == LoginFailureOutcome.LockedOut
                    ? LoginFailureReason.LockedOut
                    : LoginFailureReason.InvalidCredentials,
                null,
                account.LockedUntil);
        }

        // ---- 5) 惰性重哈希 ----
        if (verify.NeedsRehash)
        {
            account.ChangePassword(
                await _passwordService.HashAsync(command.Password, ct),
                now);
            _logger.LogInformation(
                "用户 {User} 密码已从 {Legacy} 迁移到当前算法",
                command.UserName,
                verify.UsedLegacyAlgorithm ?? "旧参数");
        }

        // ---- 6) 登录成功 ----
        account.RecordLoginSuccess(now);
        await _accounts.UpdateAsync(account, ct);

        var session = await _authorization.GetSessionAsync(account.Id);
        await WriteLoginAudit(account, sourceClient, sourceIp, "登录成功");
        _logger.LogInformation("登录成功：{User}（{sourceClient} {Ip}）", command.UserName, sourceClient, sourceIp);

        return new LoginResult(true, null, session);
    }

    public async Task LogoutAsync(
        long accountId, string sourceClient, string sourceIp, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is not null)
        {
            await WriteLoginAudit(account, sourceClient, sourceIp, "退出登录");
        }
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(
        ChangePasswordCommand command, string sourceClient, string sourceIp, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(command.AccountId, ct);
        if (account is null) return new ChangePasswordResult(false, "账号不存在");

        var verify = await _passwordService.VerifyAsync(
            command.OldPassword, account.PasswordHash, ct);
        if (!verify.Ok)
        {
            await WriteLoginAudit(account, sourceClient, sourceIp, "修改密码失败：旧密码错误");
            return new ChangePasswordResult(false, "旧密码错误");
        }

        account.ChangePassword(
            await _passwordService.HashAsync(command.NewPassword, ct),
            DateTime.Now);

        await _accounts.UpdateAsync(account, ct);
        await WriteLoginAudit(account, sourceClient, sourceIp, "修改密码成功");

        _logger.LogInformation("用户 {User} 修改密码成功{Legacy}",
            account.UserName,
            verify.UsedLegacyAlgorithm is null
                ? string.Empty
                : $"（旧密码为 {verify.UsedLegacyAlgorithm} 算法）");

        return new ChangePasswordResult(true);
    }

    public async Task<bool> VerifyAsync(
        Account user, string password, CancellationToken ct = default)
    {
        var result = await _passwordService.VerifyAsync(password, user.PasswordHash, ct);

        if (result.Ok && result.NeedsRehash)
        {
            user.ChangePassword(
                await _passwordService.HashAsync(password, ct),
                DateTime.Now);
            await _accounts.UpdateAsync(user, ct);

            _logger.LogInformation(
                "用户 {User} 密码通过 VerifyAsync 触发惰性重哈希", user.UserName);
        }

        return result.Ok;
    }

    private async Task WriteLoginAudit(Account account, string sourceClient, string sourceIp, string detail)
    {
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = account.UserName,
            OperatorUserId = account.UserId,
            SourceIp = sourceIp,
            SourceClient = "desktop",
            OperationType = "login",
            Target = account.UserName,
            Detail = detail,
            Result = detail.Contains("成功") ? 1 : 0,
            CreatedAt = DateTime.Now
        });
    }
}
