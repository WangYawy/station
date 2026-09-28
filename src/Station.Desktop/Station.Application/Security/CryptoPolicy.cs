using Station.Crypto;

namespace Station.Application.Security;

/// <summary>
/// 加密策略服务：读策略、切算法、轮换主密钥、提供算法实现。
///
/// 【职责合并】
///   - 策略读取
///   - 算法工厂
///   - 密钥轮换
/// </summary>
public interface ICryptoPolicyService
{
    /// <summary>按用途获取策略快照（命中缓存）。</summary>
    Task<CryptoPolicySnapshot> GetAsync(string usageCode, CancellationToken ct = default);

    /// <summary>获取全部策略。</summary>
    Task<IReadOnlyList<CryptoPolicySnapshot>> GetAllAsync(CancellationToken ct = default);

    /// <summary>更新策略（写库 + 清缓存 + 触发事件）。</summary>
    Task UpdateAsync(string usageCode, CryptoPolicyUpdate update,
                     string operatorAccount, CancellationToken ct = default);

    /// <summary>轮换主密钥（生成新版本 + 重加密所有敏感字段 + 审计）。</summary>
    Task<KeyRotationResult> RotateMasterKeyAsync(string operatorAccount, CancellationToken ct = default);

    /// <summary>获取当前主密钥版本号。</summary>
    int GetCurrentKeyVersion();

    /// <summary>策略变更事件。</summary>
    event EventHandler<CryptoPolicyChangedEventArgs>? Changed;

    /// <summary>手动刷新缓存（如平台下发后）。</summary>
    void InvalidateCache(string? usageCode = null);

    // ---- 算法实现获取（替代独立工厂） ----

    Task<IPasswordHasher> GetPasswordHasherAsync(CancellationToken ct = default);
    Task<ISigner> GetSignerAsync(string usageCode, CancellationToken ct = default);
    Task<IEncryptor> GetEncryptorAsync(string usageCode, CancellationToken ct = default);
    Task<IHasher> GetHasherAsync(string usageCode, CancellationToken ct = default);
    Task<IMacProvider> GetMacProviderAsync(string usageCode, CancellationToken ct = default);

    /// <summary>
    /// 获取记录仪绑定 MAC 密钥（从主密钥 HKDF 派生，info=HkdfInfo.RecorderBinding）。
    /// </summary>
    Task<byte[]> GetBindingMacKeyAsync(CancellationToken ct = default);
}

// =============================================================
// 策略快照 / 更新 / 事件 / 结果
// =============================================================

public sealed record CryptoPolicySnapshot(
    string UsageCode,
    string Algorithm,
    string? SecondaryAlgorithm,
    bool AllowLegacy,
    IReadOnlyList<string> LegacyAlgorithms,
    bool Enabled,
    string Source,
    DateTime UpdatedAt,
    string? UpdatedBy);

public sealed record CryptoPolicyUpdate(
    string Algorithm,
    string? SecondaryAlgorithm = null,
    bool? AllowLegacy = null,
    bool? Enabled = null);

public sealed class CryptoPolicyChangedEventArgs : EventArgs
{
    public string UsageCode { get; init; } = string.Empty;
    public string OldAlgorithm { get; init; } = string.Empty;
    public string NewAlgorithm { get; init; } = string.Empty;
    public string OperatorAccount { get; init; } = string.Empty;
}

public sealed record KeyRotationResult(
    int OldVersion,
    int NewVersion,
    int ReEncryptedCount,
    int FailedCount,
    TimeSpan Elapsed);

// =============================================================
// 出厂默认算法
// =============================================================

public static class CryptoDefaults
{
    public static readonly IReadOnlyDictionary<string, string> Primary =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CryptoUsage.Password] = CryptoAlgorithm.Sm3Pbkdf2,
            [CryptoUsage.License] = CryptoAlgorithm.Sm4Gcm,
            [CryptoUsage.FileSig] = CryptoAlgorithm.Sm3,
            [CryptoUsage.SecretField] = CryptoAlgorithm.Sm4Gcm,
            [CryptoUsage.FileEncryption] = CryptoAlgorithm.Sm4Gcm,
            [CryptoUsage.RecorderBinding] = CryptoAlgorithm.HmacSm3,
            [CryptoUsage.Reporting] = CryptoAlgorithm.Sm2Sm3
        };

    public static readonly IReadOnlyDictionary<string, string?> Secondary =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [CryptoUsage.Password] = null,
            [CryptoUsage.License] = CryptoAlgorithm.Sm2Sm3,
            [CryptoUsage.FileSig] = CryptoAlgorithm.Sm2Sm3,
            [CryptoUsage.SecretField] = null,
            [CryptoUsage.FileEncryption] = null,
            [CryptoUsage.RecorderBinding] = null,
            [CryptoUsage.Reporting] = null
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> LegacyAlgorithms =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [CryptoUsage.Password] = new[] { CryptoAlgorithm.Md5, CryptoAlgorithm.Sm3Pbkdf2 },
            [CryptoUsage.License] = new[] { CryptoAlgorithm.RsaSha256 },
            [CryptoUsage.FileSig] = new[] { CryptoAlgorithm.Sha256 },
            [CryptoUsage.SecretField] = new[] { CryptoAlgorithm.AesGcm },
            [CryptoUsage.FileEncryption] = new[] { CryptoAlgorithm.AesGcm },
            [CryptoUsage.RecorderBinding] = new[] { CryptoAlgorithm.HmacSha256 },
            [CryptoUsage.Reporting] = new[] { CryptoAlgorithm.RsaSha256 }
        };

    public const bool DefaultAllowLegacy = true;

    public static IReadOnlyList<string> GetDefaultLegacy(string usageCode) =>
        LegacyAlgorithms.TryGetValue(usageCode, out var list) ? list : Array.Empty<string>();
}
