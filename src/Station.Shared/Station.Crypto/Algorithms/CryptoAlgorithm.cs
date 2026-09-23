namespace Station.Crypto.Algorithms;

/// <summary>算法标识常量。</summary>
public static class CryptoAlgorithm
{
    // 密码哈希
    public const string Sm3Pbkdf2 = "PBKDF2-HMAC-SM3";
    public const string Pbkdf2Sha256 = "PBKDF2-SHA256";
    public const string Md5 = "MD5";

    // 签名
    public const string Sm2Sm3 = "SM2-SM3";
    public const string RsaSha256 = "RSA-SHA256";

    // 对称加密
    public const string Sm4Gcm = "SM4-GCM";
    public const string AesGcm = "AES-256-GCM";

    // 摘要
    public const string Sm3 = "SM3";
    public const string Sha256 = "SHA-256";

    // MAC
    public const string HmacSm3 = "HMAC-SM3";
    public const string HmacSha256 = "HMAC-SHA256";
}
