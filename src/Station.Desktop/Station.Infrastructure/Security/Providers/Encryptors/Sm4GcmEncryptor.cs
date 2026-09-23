using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security.Providers.Encryptors;

/// <summary>
/// SM4-GCM 对称加解密（国密）。
/// 密文格式：v{密钥版本}:base64(12字节 nonce || ciphertext || 16字节 tag)。
/// 写入使用当前密钥版本；读取按密文前缀的版本号取密钥，支持在线轮换。
/// AAD 建议填字段全名（如 "storage.ftpPassword"）防密文替换攻击。
/// </summary>
public sealed class Sm4GcmEncryptor : IEncryptor
{
    private const int NonceSize = 12;    // 96-bit nonce（GCM 推荐）
    private const int TagBits = 128;     // 认证标签 128 位
    private const int KeySizeBytes = 32; // SM4 密钥 32 字节（含 GCM 扩展）

    private readonly IMasterKeyProvider _keyProvider;

    public Sm4GcmEncryptor(IMasterKeyProvider keyProvider) => _keyProvider = keyProvider;

    public string Algorithm => CryptoAlgorithm.Sm4Gcm;

    public string Encrypt(string plaintext, string? aad = null)
    {
        var version = _keyProvider.CurrentVersion;
        var key = _keyProvider.GetKey(version);
        EnsureKeySize(key);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);
        var pt = Encoding.UTF8.GetBytes(plaintext);

        var cipher = new GcmBlockCipher(new SM4Engine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), TagBits, nonce, aadBytes));

        var ct = new byte[cipher.GetOutputSize(pt.Length)];
        var len = cipher.ProcessBytes(pt, 0, pt.Length, ct, 0);
        cipher.DoFinal(ct, len);

        // 打包：nonce || ciphertext+tag
        var packed = new byte[nonce.Length + ct.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(ct, 0, packed, nonce.Length, ct.Length);

        return $"v{version}:{Convert.ToBase64String(packed)}";
    }

    public string Decrypt(string ciphertext, string? aad = null)
    {
        var (version, raw) = ParseEnvelope(ciphertext);
        var key = _keyProvider.GetKey(version);
        EnsureKeySize(key);

        if (raw.Length < NonceSize + 16)
            throw new CryptographicException("密文长度不足（至少 nonce+tag）");

        var nonce = raw.AsSpan(0, NonceSize).ToArray();
        var ct = raw.AsSpan(NonceSize).ToArray();
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);

        var cipher = new GcmBlockCipher(new SM4Engine());
        cipher.Init(false, new AeadParameters(new KeyParameter(key), TagBits, nonce, aadBytes));

        var pt = new byte[cipher.GetOutputSize(ct.Length)];
        var len = cipher.ProcessBytes(ct, 0, ct.Length, pt, 0);
        cipher.DoFinal(pt, len);

        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>解析 v{n}:base64 信封。</summary>
    internal static (int version, byte[] raw) ParseEnvelope(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext) || ciphertext[0] != 'v')
            throw new FormatException("密文格式错误：缺少版本前缀");

        var idx = ciphertext.IndexOf(':');
        if (idx < 2) throw new FormatException("密文格式错误：版本分隔符缺失");
        if (!int.TryParse(ciphertext.AsSpan(1, idx - 1), out var version))
            throw new FormatException("密文格式错误：版本号非数字");

        var raw = Convert.FromBase64String(ciphertext[(idx + 1)..]);
        return (version, raw);
    }

    private static void EnsureKeySize(byte[] key)
    {
        if (key.Length < 16)
            throw new CryptographicException($"SM4 密钥长度不足：{key.Length} 字节（至少 16）");
    }
}
