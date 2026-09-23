using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Station.Crypto.Abstractions;
using Station.Crypto.Algorithms;

namespace Station.Crypto.Providers.Encryptors;

/// <summary>SM4-GCM。密文格式：v{版本}:base64(nonce(12) || ct || tag(16))。</summary>
public sealed class Sm4GcmEncryptor : IEncryptor
{
    private const int NonceSize = 12;
    private const int TagBits = 128;

    private readonly Func<int, byte[]> _keyProvider;
    private readonly Func<int> _currentVersionProvider;

    /// <param name="currentVersionProvider">返回当前密钥版本号。</param>
    /// <param name="keyProvider">按版本号取 32 字节密钥。</param>
    public Sm4GcmEncryptor(Func<int> currentVersionProvider, Func<int, byte[]> keyProvider)
    {
        _currentVersionProvider = currentVersionProvider;
        _keyProvider = keyProvider;
    }

    public string Algorithm => CryptoAlgorithm.Sm4Gcm;

    public string Encrypt(string plaintext, string? aad = null)
    {
        var version = _currentVersionProvider();
        var key = _keyProvider(version);
        return EncryptWithKey(plaintext, aad, key, version);
    }

    public string Decrypt(string ciphertext, string? aad = null)
    {
        var (version, raw) = ParseEnvelope(ciphertext);
        var key = _keyProvider(version);
        return DecryptWithKey(raw, aad, key);
    }

    internal static string EncryptWithKey(string plaintext, string? aad, byte[] key, int version)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);
        var pt = Encoding.UTF8.GetBytes(plaintext);

        var cipher = new GcmBlockCipher(new SM4Engine());
        cipher.Init(true, new AeadParameters(new KeyParameter(PadKey(key)), TagBits, nonce, aadBytes));

        var ct = new byte[cipher.GetOutputSize(pt.Length)];
        var len = cipher.ProcessBytes(pt, 0, pt.Length, ct, 0);
        cipher.DoFinal(ct, len);

        var packed = new byte[nonce.Length + ct.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(ct, 0, packed, nonce.Length, ct.Length);

        return $"v{version}:{Convert.ToBase64String(packed)}";
    }

    internal static string DecryptWithKey(byte[] raw, string? aad, byte[] key)
    {
        if (raw.Length < NonceSize + 16)
            throw new CryptographicException("密文长度不足");

        var nonce = raw.AsSpan(0, NonceSize).ToArray();
        var ct = raw.AsSpan(NonceSize).ToArray();
        var aadBytes = aad is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(aad);

        var cipher = new GcmBlockCipher(new SM4Engine());
        cipher.Init(false, new AeadParameters(new KeyParameter(PadKey(key)), TagBits, nonce, aadBytes));

        var pt = new byte[cipher.GetOutputSize(ct.Length)];
        var len = cipher.ProcessBytes(ct, 0, ct.Length, pt, 0);
        cipher.DoFinal(pt, len);

        return Encoding.UTF8.GetString(pt);
    }

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

    /// <summary>SM4 需要 16 字节密钥；给 32 字节时取前 16 字节。</summary>
    private static byte[] PadKey(byte[] key) => key.Length == 16 ? key : key[..16];
}
