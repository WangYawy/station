using Microsoft.Extensions.Logging;
using Station.Application.Security;
using Station.Crypto.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

/// <summary>密码服务实现。</summary>
public sealed class PasswordService : IPasswordService
{
    private readonly ICryptoPolicyService _policy;
    private readonly ILogger<PasswordService> _logger;

    public PasswordService(ICryptoPolicyService policy, ILogger<PasswordService> logger)
    {
        _policy = policy;
        _logger = logger;
    }

    public async Task<string> HashAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("密码不能为空", nameof(password));

        var policy = await _policy.GetAsync(CryptoUsage.Password, ct).ConfigureAwait(false);
        var hasher = await _policy.GetPasswordHasherAsync(ct).ConfigureAwait(false);
        return hasher.Hash(password);
    }

    public async Task<PasswordVerifyResult> VerifyAsync(
        string password, string storedHash, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return new PasswordVerifyResult(false, false, null);

        var policy = await _policy.GetAsync(CryptoUsage.Password, ct).ConfigureAwait(false);
        var current = await _policy.GetPasswordHasherAsync(ct).ConfigureAwait(false);

        // 1) 当前算法
        if (current.Verify(password, storedHash))
            return new PasswordVerifyResult(true, current.NeedsRehash(storedHash), null);

        // 2) Legacy 回退
        if (policy.AllowLegacy && policy.LegacyAlgorithms.Count > 0)
        {
            foreach (var legacyAlgo in policy.LegacyAlgorithms)
            {
                if (string.Equals(legacyAlgo, policy.Algorithm, StringComparison.OrdinalIgnoreCase))
                    continue;

                IPasswordHasher legacy;
                try
                {
                    legacy = Station.Infrastructure.Security.Internal.AlgorithmResolver
                        .ResolvePasswordHasher(legacyAlgo);
                }
                catch (NotSupportedException)
                {
                    _logger.LogWarning("策略配置的 legacy 算法 {Algo} 未注册，已跳过", legacyAlgo);
                    continue;
                }

                if (legacy.Verify(password, storedHash))
                {
                    _logger.LogInformation("密码校验命中 legacy 算法 {Algo}", legacyAlgo);
                    return new PasswordVerifyResult(true, true, legacyAlgo);
                }
            }
        }

        return new PasswordVerifyResult(false, false, null);
    }
}
