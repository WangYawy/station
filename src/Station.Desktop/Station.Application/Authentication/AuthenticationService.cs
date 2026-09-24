using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Application.Security;
using Station.Data.Repositories;
using Station.Domain.Entities;

namespace Station.Application.Authentication;

/// <summary>
/// 登录认证服务。
/// 
/// 【依赖简化】
///   所有密码哈希相关工作（生成/校验/legacy 回退）统一委托给 IPasswordService，
///   本服务不再直接依赖 ICryptoPolicyService / ICryptoProviderFactory / IPasswordHasher。
/// 
/// 【惰性重哈希】
///   登录成功后，若 IPasswordService 返回 NeedsRehash=true，
///   本服务立即用当前算法重写哈希并落库（与登录成功同一个 Update 事务中）。
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IRepository<Account> _accounts;
    private readonly IAuthorizationService _authorization;
    private readonly IAuditLogService _audit;
    private readonly AuthOptions _options;
    private readonly IPasswordService _passwordService;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IRepository<Account> accounts,
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

    // =========================================================
    // 登录
    // =========================================================

    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        // ---- 1) 账号存在性 ----
        var account = await _accounts.FirstAsync(a => a.UserName == request.UserName);
        if (account is null)
        {
            _logger.LogWarning("登录失败：账号 {User} 不存在（IP {Ip}）",
                request.UserName, request.SourceIp);
            await _audit.WriteAsync(new AuditLog
            {
                OperatorAccount = request.UserName,
                SourceIp = request.SourceIp,
                OperationType = "login",
                Target = request.UserName,
                Detail = "登录失败：账号不存在",
                SourceClient = "desktop",
                Result = 0,
                CreatedAt = DateTime.Now
            });
            return new LoginResult(false, LoginFailureReason.InvalidCredentials, null);
        }

        // ---- 2) 账号启用状态 ----
        if (!account.IsEnabled)
        {
            await WriteLoginAudit(account, request.SourceIp, "登录失败：账号已禁用");
            return new LoginResult(false, LoginFailureReason.Disabled, null);
        }

        // ---- 3) 锁定状态 ----
        if (account.LockedUntil is { } lockedUntil && lockedUntil > DateTime.Now)
        {
            _logger.LogWarning("登录失败：账号 {User} 锁定至 {Until}",
                request.UserName, lockedUntil);
            await WriteLoginAudit(account, request.SourceIp,
                $"登录失败：账号锁定至 {lockedUntil:HH:mm:ss}");
            return new LoginResult(false, LoginFailureReason.LockedOut, null, lockedUntil);
        }

        // ---- 4) 密码校验（走门面，内含 legacy 回退） ----
        var verify = await _passwordService.VerifyAsync(request.Password, account.PasswordHash);
        if (!verify.Ok)
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

        // ---- 5) 惰性重哈希（legacy 或参数过期） ----
        if (verify.NeedsRehash)
        {
            var newHash = await _passwordService.HashAsync(request.Password);
            account.PasswordHash = newHash;
            _logger.LogInformation(
                "用户 {User} 密码已从 {Legacy} 迁移到当前算法",
                request.UserName,
                verify.UsedLegacyAlgorithm ?? "旧参数");
        }

        // ---- 6) 登录成功：重置计数，签发会话 ----
        account.FailedLoginAttempts = 0;
        account.LockedUntil = null;
        await _accounts.UpdateAsync(account);

        var session = await _authorization.GetSessionAsync(account.Id);
        await WriteLoginAudit(account, request.SourceIp, "登录成功");
        _logger.LogInformation("登录成功：{User}（IP {Ip}）", request.UserName, request.SourceIp);

        return new LoginResult(true, null, session);
    }

    // =========================================================
    // 登出
    // =========================================================

    public async Task LogoutAsync(long accountId, string? sourceIp = null)
    {
        var account = await _accounts.GetByIdAsync(accountId);
        if (account is not null)
        {
            await WriteLoginAudit(account, sourceIp, "退出登录");
        }
    }

    // =========================================================
    // 改密（本次修复 legacy 回退缺失的 Bug）
    // =========================================================

    /// <summary>
    /// 修改密码。
    /// 
    /// 【Bug 修复】
    ///   旧实现只按当前策略算法校验旧密码，导致 MD5 存量用户无法改密。
    ///   现改为走 IPasswordService.VerifyAsync，自动 legacy 回退。
    /// </summary>
    public async Task<ChangePasswordResult> ChangePasswordAsync(
        long accountId, string oldPassword, string newPassword)
    {
        var account = await _accounts.GetByIdAsync(accountId);
        if (account is null)
        {
            return new ChangePasswordResult(false, "账号不存在");
        }

        // 走门面：内含当前算法 + legacy 回退
        var verify = await _passwordService.VerifyAsync(oldPassword, account.PasswordHash);
        if (!verify.Ok)
        {
            await WriteLoginAudit(account, null, "修改密码失败：旧密码错误");
            return new ChangePasswordResult(false, "旧密码错误");
        }

        // 用当前算法生成新哈希
        account.PasswordHash = await _passwordService.HashAsync(newPassword);
        await _accounts.UpdateAsync(account);
        await WriteLoginAudit(account, null, "修改密码成功");

        _logger.LogInformation("用户 {User} 修改密码成功{Legacy}",
            account.UserName,
            verify.UsedLegacyAlgorithm is null
                ? string.Empty
                : $"（旧密码为 {verify.UsedLegacyAlgorithm} 算法）");

        return new ChangePasswordResult(true);
    }

    // =========================================================
    // 对外校验（供其他模块调用，如平台指令二次验证）
    // =========================================================

    /// <summary>
    /// 校验指定账号的密码（供外部模块调用）。
    /// 内含惰性重哈希：校验通过且需要重哈希时，自动写库。
    /// </summary>
    public async Task<bool> VerifyAsync(
        Account user, string password, CancellationToken ct = default)
    {
        var result = await _passwordService.VerifyAsync(password, user.PasswordHash, ct);

        if (result.Ok && result.NeedsRehash)
        {
            user.PasswordHash = await _passwordService.HashAsync(password, ct);
            await _accounts.UpdateAsync(user);
            _logger.LogInformation("用户 {User} 密码通过 VerifyAsync 触发惰性重哈希",
                user.UserName);
        }

        return result.Ok;
    }

    // =========================================================
    // 审计辅助
    // =========================================================

    private async Task WriteLoginAudit(Account account, string? sourceIp, string detail)
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
