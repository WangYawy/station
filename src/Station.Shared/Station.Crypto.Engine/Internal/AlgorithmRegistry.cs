using Station.Crypto.Engine.Encryptors;
using Station.Crypto.Engine.Hashers;
using Station.Crypto.Engine.Macs;
using Station.Crypto.Engine.PasswordHashers;
using Station.Crypto.Engine.Signers;

namespace Station.Crypto.Engine.Internal;

/// <summary>
/// 算法注册表实现（唯一 switch）。
///
/// 【使用方式】
///   - Tools：AlgorithmRegistry.Default（静态单例）
///   - Application / Infrastructure：通过 DI 注入 ICryptoAlgorithmRegistry
///
/// ⚠️ 所有算法解析必须走此实现，禁止在业务代码里散落 switch。
/// </summary>
public sealed class AlgorithmRegistry : ICryptoAlgorithmRegistry
{
    /// <summary>默认共享实例（无状态、线程安全）。</summary>
    public static readonly AlgorithmRegistry Default = new();

    public IPasswordHasher GetPasswordHasher(string algorithm) =>
        algorithm?.ToUpperInvariant() switch
        {
            "PBKDF2-HMAC-SM3" => new Sm3Pbkdf2Hasher(),
            "PBKDF2-SHA256" => new Pbkdf2Sha256Hasher(),
            "MD5" => new Md5LegacyHasher(),
            _ => throw new NotSupportedException($"未注册的密码哈希算法：{algorithm}")
        };

    public ISigner GetSigner(string algorithm) =>
        algorithm?.ToUpperInvariant() switch
        {
            "SM2-SM3" => new Sm2Sm3Signer(),
            "RSA-SHA256" => new RsaSha256Signer(),
            _ => throw new NotSupportedException($"未注册的签名算法：{algorithm}")
        };

    public IHasher GetHasher(string algorithm) =>
        algorithm?.ToUpperInvariant() switch
        {
            "SM3" => new Sm3Hasher(),
            "SHA-256" => new Sha256Hasher(),
            _ => throw new NotSupportedException($"未注册的摘要算法：{algorithm}")
        };

    public IMacProvider GetMacProvider(string algorithm) =>
        algorithm?.ToUpperInvariant() switch
        {
            "HMAC-SM3" => new HmacSm3Provider(),
            "HMAC-SHA256" => new HmacSha256Provider(),
            _ => throw new NotSupportedException($"未注册的 MAC 算法：{algorithm}")
        };

    public IEncryptor GetEncryptor(string algorithm, IMasterKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return algorithm?.ToUpperInvariant() switch
        {
            "SM4-GCM" => new Sm4GcmEncryptor(keys),
            "AES-256-GCM" => new AesGcmEncryptor(keys),
            _ => throw new NotSupportedException($"未注册的加密算法：{algorithm}")
        };
    }
}
