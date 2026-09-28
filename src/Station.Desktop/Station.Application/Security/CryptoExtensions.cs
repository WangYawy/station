using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Station.Crypto;

namespace Station.Application.Security;

/// <summary>
/// 敏感配置字段加解密扩展方法。
///
/// 【AAD 约定】由 <see cref="CryptoAad.BuildFieldAad"/> 统一派生，防止密文跨字段替换。
/// 【密文格式】v{版本}:base64(...)，支持历史明文兼容。
/// </summary>
public static class CryptoExtensions
{
    /// <summary>判断字符串是否为本系统密文格式（v{n}:base64）。</summary>
    public static bool IsSecretProtected(this ICryptoPolicyService policy, string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length < 3) return false;
        if (value[0] != 'v') return false;
        if (!char.IsDigit(value[1])) return false;

        var sepIndex = value.IndexOf(':');
        return sepIndex >= 2 && sepIndex <= 5;
    }

    /// <summary>
    /// 加密敏感字段。失败向上抛（写不进去就是写不进去）。
    /// </summary>
    public static async Task<string> ProtectSecretAsync(
        this ICryptoPolicyService policy,
        string group,
        string key,
        string plaintext,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(plaintext);

        var log = logger ?? NullLogger.Instance;

        try
        {
            var encryptor = await policy
                .GetEncryptorAsync(CryptoUsage.SecretField, ct)
                .ConfigureAwait(false);
            var aad = CryptoAad.BuildFieldAad(group, key);
            var cipher = encryptor.Encrypt(plaintext, aad);
            log.LogDebug("已加密敏感字段 {Group}.{Key}", group, key);
            return cipher;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "加密敏感字段 {Group}.{Key} 失败", group, key);
            throw;
        }
    }

    /// <summary>
    /// 解密敏感字段（尽力而为，不抛异常）。
    ///
    /// 行为：
    ///   - null/空 → 原样返回；
    ///   - 非密文 → 原样返回 + WARN；
    ///   - 密文解密失败 → null + ERROR。
    /// </summary>
    public static async Task<string?> TryUnprotectSecretAsync(
        this ICryptoPolicyService policy,
        string group,
        string key,
        string? cipher,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var log = logger ?? NullLogger.Instance;

        if (string.IsNullOrEmpty(cipher)) return cipher;

        if (!policy.IsSecretProtected(cipher))
        {
            log.LogWarning(
                "敏感字段 {Group}.{Key} 存储的是历史明文，建议尽快重写以完成加密迁移",
                group, key);
            return cipher;
        }

        try
        {
            var encryptor = await policy
                .GetEncryptorAsync(CryptoUsage.SecretField, ct)
                .ConfigureAwait(false);
            var aad = CryptoAad.BuildFieldAad(group, key);
            return encryptor.Decrypt(cipher, aad);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "解密敏感字段 {Group}.{Key} 失败", group, key);
            return null;
        }
    }
}
