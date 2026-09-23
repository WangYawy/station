using System.Security.Cryptography;
using System.Text;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security.Providers.Encryptors;

/// <summary>
/// AES-256-GCM 对称加解密（非国密备选）。密文格式与 SM4-GCM 一致。
/// </summary>
public sealed class AesGcmEncryptor : IEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IMasterKeyProvider _keyProvider;

    public AesGcmEncryptor(IMasterKeyProvider keyProvider) => _keyProvider = keyProvider;

    public string Algorithm => CryptoAlgorithm.AesGcm;

    public string Encrypt(string plaintext, string? aad = null)
    {
        var version = _keyProvider.CurrentVersion;
        var key = _keyProvider.GetKey(version);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, pt, ct, tag, aadBytes);

        // 打包：nonce || ciphertext || tag
        var packed = new byte[nonce.Length + ct.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(ct, 0, packed, nonce.Length, ct.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length + ct.Length, tag.Length);

        return $"v{version}:{Convert.ToBase64String(packed)}";
    }

    public string Decrypt(string ciphertext, string? aad = null)
    {
        var (version, raw) = Sm4GcmEncryptor.ParseEnvelope(ciphertext);
        var key = _keyProvider.GetKey(version);

        if (raw.Length < NonceSize + TagSize)
            throw new CryptographicException("密文长度不足");

        var nonce = raw.AsSpan(0, NonceSize);
        var tag = raw.AsSpan(raw.Length - TagSize, TagSize);
        var ct = raw.AsSpan(NonceSize, raw.Length - NonceSize - TagSize);

        var pt = new byte[ct.Length];
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ct, tag, pt, aadBytes);

        return Encoding.UTF8.GetString(pt);
    }
}
