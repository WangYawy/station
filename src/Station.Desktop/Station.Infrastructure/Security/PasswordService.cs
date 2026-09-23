using Microsoft.Extensions.Logging;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

/// <summary>
/// 密码服务实现（基于 ICryptoPolicyService + ICryptoProviderFactory）。
/// 
/// 【算法路由】
///   - HashAsync：读策略 → factory.GetPasswordHasher(策略算法) → Hash；
///   - VerifyAsync：读策略 → 当前算法校验 → 失败则按 LegacyAlgorithms 列表依次回退。
/// 
/// 【惰性重哈希触发条件】
///   1) 校验命中当前算法但 NeedsRehash=true（同算法参数升级，如迭代数提高）；
///   2) 校验命中 legacy 算法（跨算法迁移）。
///   两种情况都返回 NeedsRehash=true，由调用方决定是否立即写库。
/// </summary>
public sealed class PasswordService : IPasswordService
{
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoProviderFactory _factory;
    private readonly ILogger<PasswordService> _logger;

    public PasswordService(
        ICryptoPolicyService policy,
        ICryptoProviderFactory factory,
        ILogger<PasswordService> logger)
    {
        _policy = policy;
        _factory = factory;
        _logger = logger;
    }

    // =========================================================
    // 生成哈希
    // =========================================================

    /// <inheritdoc />
    public async Task<string> HashAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("密码不能为空", nameof(password));

        // 读密码策略（默认 PBKDF2-HMAC-SM3）
        var policy = await _policy.GetAsync(CryptoUsage.Password, ct).ConfigureAwait(false);

        // 拿对应算法实现
        var hasher = _factory.GetPasswordHasher(policy.Algorithm);

        // 执行哈希（产出带算法前缀的字符串，如 "sm3$10000$..."）
        var hash = hasher.Hash(password);

        _logger.LogDebug("已按算法 {Algorithm} 生成密码哈希", policy.Algorithm);
        return hash;
    }

    // =========================================================
    // 校验密码
    // =========================================================

    /// <inheritdoc />
    public async Task<PasswordVerifyResult> VerifyAsync(
        string password,
        string storedHash,
        CancellationToken ct = default)
    {
        // 空输入直接失败，不走后续流程（避免无意义的策略读取）
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return new PasswordVerifyResult(false, false, null);

        var policy = await _policy.GetAsync(CryptoUsage.Password, ct).ConfigureAwait(false);

        // ---- 步骤 1：按当前策略算法校验 ----
        var current = _factory.GetPasswordHasher(policy.Algorithm);
        if (current.Verify(password, storedHash))
        {
            // 算法一致，但参数可能过期（如迭代数从 10000 升到 60000）
            var needsRehash = current.NeedsRehash(storedHash);
            if (needsRehash)
            {
                _logger.LogDebug("密码校验命中当前算法 {Algorithm}，但参数需升级，建议重哈希",
                    policy.Algorithm);
            }
            return new PasswordVerifyResult(true, needsRehash, null);
        }

        // ---- 步骤 2：迁移期 legacy 回退 ----
        // 从策略表的 LegacyAlgorithms 列表依次尝试（由 CryptoDefaults 提供默认，可在 DB 覆盖）
        if (policy.AllowLegacy && policy.LegacyAlgorithms.Count > 0)
        {
            foreach (var legacyAlgo in policy.LegacyAlgorithms)
            {
                // 跳过与当前算法重复的项（例如策略表里同时列了 SM3 又列了 SM3 迭代参数）
                if (string.Equals(legacyAlgo, policy.Algorithm, StringComparison.OrdinalIgnoreCase))
                    continue;

                IPasswordHasher legacy;
                try
                {
                    legacy = _factory.GetPasswordHasher(legacyAlgo);
                }
                catch (NotSupportedException)
                {
                    // 策略表配置了未注册的算法：记日志，跳过（不阻断登录）
                    _logger.LogWarning(
                        "策略表配置的 legacy 算法 {Algo} 未在 factory 注册，已跳过",
                        legacyAlgo);
                    continue;
                }

                if (legacy.Verify(password, storedHash))
                {
                    _logger.LogInformation(
                        "密码校验命中 legacy 算法 {Algo}，建议用当前算法 {Current} 重哈希",
                        legacyAlgo, policy.Algorithm);
                    return new PasswordVerifyResult(true, true, legacyAlgo);
                }
            }
        }

        // 全部失败
        return new PasswordVerifyResult(false, false, null);
    }
}
