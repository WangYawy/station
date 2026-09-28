using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Station.Crypto.Engine.Internal;

namespace Station.Crypto.Engine.Encryptors;

/// <summary>
/// SM4-GCM。密文格式：v{版本}:base64(nonce(12) || ct || tag(16))。
///
/// 【密钥约定】SM4 使用 16 字节密钥。若传入的密钥 > 16 字节，
///            取前 16 字节（与历史行为一致）。
/// </summary>
public sealed class Sm4GcmEncryptor : IEncryptor
{
    private const int NonceSize = 12;
    private const int TagBits = 128;

    private readonly IMasterKeyProvider _keys;

    public Sm4GcmEncryptor(IMasterKeyProvider keys)
        => _keys = keys ?? throw new ArgumentNullException(nameof(keys));

    public string Algorithm => CryptoAlgorithm.Sm4Gcm;

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

    /// <summary>对给定密钥加密（供内部/测试使用）。</summary>
    internal static string EncryptWithKey(string plaintext, string? aad, byte[] key, int version)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);
        var pt = Encoding.UTF8.GetBytes(plaintext);

        var cipher = new GcmBlockCipher(new SM4Engine());
        cipher.Init(true, new AeadParameters(new KeyParameter(NormalizeKey(key)), TagBits, nonce, aadBytes));

        var ct = new byte[cipher.GetOutputSize(pt.Length)];
        var len = cipher.ProcessBytes(pt, 0, pt.Length, ct, 0);
        cipher.DoFinal(ct, len);

        var packed = new byte[nonce.Length + ct.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(ct, 0, packed, nonce.Length, ct.Length);

        return CipherEnvelope.Wrap(version, packed);
    }

    /// <summary>对给定密钥解密（供内部/测试使用）。</summary>
    internal static string DecryptWithKey(byte[] raw, string? aad, byte[] key)
    {
        if (raw.Length < NonceSize + TagBits / 8)
            throw new CipherFormatException("密文长度不足");

        var nonce = raw.AsSpan(0, NonceSize).ToArray();
        var ct = raw.AsSpan(NonceSize).ToArray();
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);

        var cipher = new GcmBlockCipher(new SM4Engine());
        cipher.Init(false, new AeadParameters(new KeyParameter(NormalizeKey(key)), TagBits, nonce, aadBytes));

        var pt = new byte[cipher.GetOutputSize(ct.Length)];
        try
        {
            var len = cipher.ProcessBytes(ct, 0, ct.Length, pt, 0);
            cipher.DoFinal(pt, len);
        }
        catch (InvalidCipherTextException ex)
        {
            throw new DecryptionFailedException(
                "SM4-GCM 解密失败：密钥错误 / 密文损坏 / AAD 不匹配", ex);
        }

        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>SM4 需要 16 字节密钥；给更长时取前 16 字节。</summary>
    private static byte[] NormalizeKey(byte[] key)
        => key.Length == 16 ? key : key[..16];
}
