using System.Security.Cryptography;
using System.Text;
using Station.Crypto.Engine.Internal;

namespace Station.Crypto.Engine.Encryptors;

/// <summary>
/// AES-256-GCM。密文格式与 SM4-GCM 一致。
///
/// 【密钥约定】AES-256 必须使用 32 字节密钥。
///            若传入的密钥长度不是 32，抛 CryptoException。
/// </summary>
public sealed class AesGcmEncryptor : IEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IMasterKeyProvider _keys;

    public AesGcmEncryptor(IMasterKeyProvider keys)
        => _keys = keys ?? throw new ArgumentNullException(nameof(keys));

    public string Algorithm => CryptoAlgorithm.AesGcm;

    public string Encrypt(string plaintext, string? aad = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var version = _keys.CurrentVersion;
        var key = _keys.GetKey(version);
        return EncryptWithKey(plaintext, aad, key, version);
    }

    public string Decrypt(string ciphertext, string? aad = null)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        var (version, raw) = CipherEnvelope.Parse(ciphertext);
        var key = _keys.GetKey(version);
        return DecryptWithKey(raw, aad, key);
    }

    internal static string EncryptWithKey(string plaintext, string? aad, byte[] key, int version)
    {
        var k = NormalizeKey(key);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(k, TagSize);
        aes.Encrypt(nonce, pt, ct, tag, aadBytes);

        var packed = new byte[nonce.Length + ct.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(ct, 0, packed, nonce.Length, ct.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length + ct.Length, tag.Length);

        return CipherEnvelope.Wrap(version, packed);
    }

    internal static string DecryptWithKey(byte[] raw, string? aad, byte[] key)
    {
        if (raw.Length < NonceSize + TagSize)
            throw new CipherFormatException("密文长度不足");

        var k = NormalizeKey(key);
        var nonce = raw.AsSpan(0, NonceSize);
        var tag = raw.AsSpan(raw.Length - TagSize, TagSize);
        var ct = raw.AsSpan(NonceSize, raw.Length - NonceSize - TagSize);

        var pt = new byte[ct.Length];
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);

        try
        {
            using var aes = new AesGcm(k, TagSize);
            aes.Decrypt(nonce, ct, tag, pt, aadBytes);
        }
        catch (CryptographicException ex)
        {
            throw new DecryptionFailedException(
                "AES-256-GCM 解密失败：密钥错误 / 密文损坏 / AAD 不匹配", ex);
        }

        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>AES-256 必须使用 32 字节密钥。</summary>
    private static byte[] NormalizeKey(byte[] key)
    {
        if (key.Length == 32) return key;
        throw new CryptoException($"AES-256-GCM 需要 32 字节密钥，当前 {key.Length} 字节");
    }
}
