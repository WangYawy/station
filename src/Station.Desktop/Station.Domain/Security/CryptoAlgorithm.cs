namespace Station.Domain.Security;

/// <summary>
/// 算法标识常量。所有可切换算法的唯一名称，持久化到 station_crypto_policy.Algorithm。
/// 命名约定：大写 + 连字符。新增算法必须在此登记，且在 CryptoProviderFactory 中注册实现。
/// </summary>
public static class CryptoAlgorithm
{
    // ---- 密码哈希类 ----
    /// <summary>PBKDF2-HMAC-SM3（国密首选，默认）。存储格式：sm3$iter$salt$hash</summary>
    public const string Sm3Pbkdf2 = "PBKDF2-HMAC-SM3";

    /// <summary>MD5（仅用于兼容旧数据，不推荐新写入）。</summary>
    public const string Md5 = "MD5";

    /// <summary>PBKDF2-SHA256（非国密备选）。存储格式：pbkdf2-sha256$iter$salt$hash</summary>
    public const string Pbkdf2Sha256 = "PBKDF2-SHA256";

    // ---- 签名类 ----
    /// <summary>SM2 签名 + SM3 摘要（国密首选）。</summary>
    public const string Sm2Sm3 = "SM2-SM3";

    /// <summary>RSA 签名 + SHA256 摘要（非国密备选）。</summary>
    public const string RsaSha256 = "RSA-SHA256";

    // ---- 对称加密类 ----
    /// <summary>SM4-GCM（国密首选，带认证标签）。</summary>
    public const string Sm4Gcm = "SM4-GCM";

    /// <summary>AES-256-GCM（非国密备选）。</summary>
    public const string AesGcm = "AES-256-GCM";

    // ---- 对称 MAC 类 ----
    /// <summary>HMAC-SM3（国密对称 MAC，推荐）。</summary>
    public const string HmacSm3 = "HMAC-SM3";

    /// <summary>HMAC-SHA256（非国密备选）。</summary>
    public const string HmacSha256 = "HMAC-SHA256";

    // ---- 摘要类（用于文件签名）----
    /// <summary>SM3 摘要。</summary>
    public const string Sm3 = "SM3";

    /// <summary>SHA-256 摘要。</summary>
    public const string Sha256 = "SHA-256";


}
