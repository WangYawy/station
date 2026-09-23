using Station.Domain.Security;

namespace Station.Application.Security.Models;

/// <summary>
/// 出厂默认算法配置（DB 策略缺失时的兜底）。
/// </summary>
public static class CryptoDefaults
{
    /// <summary>每用途的默认主算法。</summary>
    public static readonly IReadOnlyDictionary<string, string> Primary =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CryptoUsage.Password] = CryptoAlgorithm.Sm3Pbkdf2,
            [CryptoUsage.License] = CryptoAlgorithm.Sm4Gcm,        // + SM2-SM3 签名
            [CryptoUsage.FileSig] = CryptoAlgorithm.Sm3,           // + SM2 签名
            [CryptoUsage.SecretField] = CryptoAlgorithm.Sm4Gcm,
            [CryptoUsage.FileEncryption] = CryptoAlgorithm.Sm4Gcm,         // 分块 SM4-GCM
            [CryptoUsage.Reporting] = CryptoAlgorithm.Sm2Sm3   // 主算法即签名算法
        };

    /// <summary>每用途的组合算法第二段（无则 null）。</summary>
    public static readonly IReadOnlyDictionary<string, string?> Secondary =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [CryptoUsage.Password] = null,
            [CryptoUsage.License] = CryptoAlgorithm.Sm2Sm3,
            [CryptoUsage.FileSig] = CryptoAlgorithm.Sm2Sm3,
            [CryptoUsage.SecretField] = null,
            [CryptoUsage.FileEncryption] = null,
            [CryptoUsage.Reporting] = null  // 主算法即签名算法
        };

    /// <summary>
    /// 每用途的迁移期 legacy 算法候选（按优先级从旧到新排列）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> LegacyAlgorithms =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [CryptoUsage.Password] = new[]
            {
                CryptoAlgorithm.Md5,
                CryptoAlgorithm.Sm3Pbkdf2
            },
            [CryptoUsage.License] = new[]
            {
                CryptoAlgorithm.RsaSha256
            },
            [CryptoUsage.FileSig] = new[]
            {
                CryptoAlgorithm.Sha256
            },
            [CryptoUsage.SecretField] = new[]
            {
                CryptoAlgorithm.AesGcm
            },
            [CryptoUsage.FileEncryption] = new[]
            {
                CryptoAlgorithm.AesGcm
            },
            [CryptoUsage.Reporting] = new[]
            {
                CryptoAlgorithm.RsaSha256
            }
        };

    /// <summary>密码迁移期默认允许旧算法回退。</summary>
    public const bool DefaultAllowLegacy = true;

    /// <summary>获取指定用途的默认 legacy 候选（找不到返回空列表）。</summary>
    public static IReadOnlyList<string> GetDefaultLegacy(string usageCode) =>
        LegacyAlgorithms.TryGetValue(usageCode, out var list)
            ? list
            : Array.Empty<string>();
}
