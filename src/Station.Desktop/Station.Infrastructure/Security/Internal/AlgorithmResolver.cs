using Station.Crypto.Abstractions;
using Station.Crypto.Algorithms;
using Station.Crypto.Providers.Encryptors;
using Station.Crypto.Providers.Hashers;
using Station.Crypto.Providers.Macs;
using Station.Crypto.Providers.PasswordHashers;
using Station.Crypto.Providers.Signers;
using Station.Infrastructure.Security.Keys;

namespace Station.Infrastructure.Security.Internal;

/// <summary>
/// 算法解析器（internal static）。
/// 用 switch 表达式实现 O(1) 路由，零反射。
/// </summary>
internal static class AlgorithmResolver
{
    public static IPasswordHasher ResolvePasswordHasher(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "PBKDF2-HMAC-SM3" => new Sm3Pbkdf2Hasher(),
            "PBKDF2-SHA256" => new Pbkdf2Sha256Hasher(),
            "MD5" => new Md5LegacyHasher(),
            _ => throw new NotSupportedException($"未注册的密码哈希算法：{algorithm}")
        };

    public static ISigner ResolveSigner(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "SM2-SM3" => new Sm2Sm3Signer(),
            "RSA-SHA256" => new RsaSha256Signer(),
            _ => throw new NotSupportedException($"未注册的签名算法：{algorithm}")
        };

    public static IHasher ResolveHasher(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "SM3" => new Sm3Hasher(),
            "SHA-256" => new Sha256Hasher(),
            _ => throw new NotSupportedException($"未注册的摘要算法：{algorithm}")
        };

    public static IMacProvider ResolveMac(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "HMAC-SM3" => new HmacSm3Provider(),
            "HMAC-SHA256" => new HmacSha256Provider(),
            _ => throw new NotSupportedException($"未注册的 MAC 算法：{algorithm}")
        };

    public static IEncryptor ResolveEncryptor(string algorithm, MasterKeyProvider keys) =>
        algorithm.ToUpperInvariant() switch
        {
            "SM4-GCM" => new Sm4GcmEncryptor(() => keys.CurrentVersion, keys.GetKey),
            "AES-256-GCM" => new AesGcmEncryptor(() => keys.CurrentVersion, keys.GetKey),
            _ => throw new NotSupportedException($"未注册的加密算法：{algorithm}")
        };
}
