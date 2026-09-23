using System.Security.Cryptography;
using System.Text;
using Station.Crypto.Abstractions;
using Station.Crypto.Algorithms;

namespace Station.Crypto.Providers.Encryptors;

/// <summary>AES-256-GCM。格式与 SM4-GCM 一致。</summary>
public sealed class AesGcmEncryptor : IEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly Func<int> _currentVersionProvider;
    private readonly Func<int, byte[]> _keyProvider;

    public AesGcmEncryptor(Func<int> currentVersionProvider, Func<int, byte[]> keyProvider)
    {
        _currentVersionProvider = currentVersionProvider;
        _keyProvider = keyProvider;
    }

    public string Algorithm => CryptoAlgorithm.AesGcm;

    public string Encrypt(string plaintext, string? aad = null)
    {
        var version = _currentVersionProvider();
        var key = _keyProvider(version);
        return EncryptWithKey(plaintext, aad, key, version);
    }

    public string Decrypt(string ciphertext, string? aad = null)
    {
        var (version, raw) = Sm4GcmEncryptor.ParseEnvelope(ciphertext);
        var key = _keyProvider(version);
        return DecryptWithKey(raw, aad, key);
    }

    internal static string EncryptWithKey(string plaintext, string? aad, byte[] key, int version)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key[..32], TagSize);
        aes.Encrypt(nonce, pt, ct, tag, aadBytes);

        var packed = new byte[nonce.Length + ct.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(ct, 0, packed, nonce.Length, ct.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length + ct.Length, tag.Length);

        return $"v{version}:{Convert.ToBase64String(packed)}";
    }

    internal static string DecryptWithKey(byte[] raw, string? aad, byte[] key)
    {
        if (raw.Length < NonceSize + TagSize)
            throw new CryptographicException("密文长度不足");

        var nonce = raw.AsSpan(0, NonceSize);
        var tag = raw.AsSpan(raw.Length - TagSize, TagSize);
        var ct = raw.AsSpan(NonceSize, raw.Length - NonceSize - TagSize);

        var pt = new byte[ct.Length];
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);

        using var aes = new AesGcm(key[..32], TagSize);
        aes.Decrypt(nonce, ct, tag, pt, aadBytes);

        return Encoding.UTF8.GetString(pt);
    }
}
