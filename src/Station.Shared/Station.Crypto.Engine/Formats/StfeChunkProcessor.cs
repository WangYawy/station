using System.Buffers.Binary;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Station.Crypto.Formats;

namespace Station.Crypto.Engine.Formats;

/// <summary>
/// STFE 单块加解密（无状态，线程安全）。
///
/// 分块格式：Nonce(12) || Ciphertext || Tag(16)
/// AAD：整个 32 字节文件头
/// </summary>
internal static class StfeChunkProcessor
{
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int TagBits = TagSize * 8;

    /// <summary>
    /// 按算法规范化密钥长度：
    ///   - SM4-GCM：    16 字节（> 16 时取前 16，与历史行为一致）
    ///   - AES-256-GCM：32 字节（长度不符抛异常）
    /// </summary>
    /// <exception cref="CryptoException">AES 密钥长度错误。</exception>
    /// <exception cref="NotSupportedException">算法未注册。</exception>
    public static byte[] NormalizeKey(byte[] key, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(key);
        return algorithm.ToUpperInvariant() switch
        {
            "SM4-GCM" => key.Length == 16 ? key : key[..16],
            "AES-256-GCM" => key.Length == 32
                ? key
                : throw new CryptoException(
                    $"AES-256-GCM 需要 32 字节密钥，当前 {key.Length} 字节"),
            _ => throw new NotSupportedException($"不支持的算法：{algorithm}")
        };
    }

    /// <summary>
    /// 构造分块 Nonce：NonceSeed(8) || chunkIndex(uint32 LE)。
    /// </summary>
    public static byte[] BuildNonce(byte[] seed, uint chunkIndex)
    {
        if (seed is null || seed.Length != 8)
            throw new ArgumentException("seed 必须为 8 字节", nameof(seed));

        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(seed, 0, nonce, 0, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(8, 4), chunkIndex);
        return nonce;
    }

    /// <summary>算法名 → 头部 AlgorithmId。</summary>
    public static byte AlgorithmIdOf(string algorithm) => algorithm.ToUpperInvariant() switch
    {
        "SM4-GCM" => StfeHeader.AlgoSm4Gcm,
        "AES-256-GCM" => StfeHeader.AlgoAesGcm,
        _ => throw new NotSupportedException($"不支持的算法：{algorithm}")
    };

    /// <summary>
    /// 加密单块，返回 (Ciphertext, Tag)。
    /// </summary>
    /// <param name="algorithm">算法名。</param>
    /// <param name="key">已规范化密钥。</param>
    /// <param name="nonce">12 字节 Nonce。</param>
    /// <param name="aad">整个 32 字节文件头。</param>
    /// <param name="plain">明文缓冲。</param>
    /// <param name="plainLen">明文有效长度。</param>
    public static (byte[] Ciphertext, byte[] Tag) Encrypt(
        string algorithm, byte[] key, byte[] nonce, byte[] aad,
        byte[] plain, int plainLen)
    {
        var cipher = CreateCipher(algorithm);
        cipher.Init(true, new AeadParameters(
            new KeyParameter(NormalizeKey(key, algorithm)), TagBits, nonce, aad));

        var output = new byte[cipher.GetOutputSize(plainLen)];
        var len = cipher.ProcessBytes(plain, 0, plainLen, output, 0);
        cipher.DoFinal(output, len);

        var ciphertext = new byte[output.Length - TagSize];
        var tag = new byte[TagSize];
        Buffer.BlockCopy(output, 0, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(output, ciphertext.Length, tag, 0, TagSize);
        return (ciphertext, tag);
    }

    /// <summary>
    /// 解密单块，写入预分配的 output。
    /// 输入是连续的密文+Tag（调用方保证）。
    /// </summary>
    /// <param name="algorithm">算法名。</param>
    /// <param name="key">已规范化密钥。</param>
    /// <param name="nonce">12 字节 Nonce。</param>
    /// <param name="aad">整个 32 字节文件头。</param>
    /// <param name="input">密文 + Tag 连续缓冲。</param>
    /// <param name="inputOffset">密文起始偏移。</param>
    /// <param name="inputLength">密文 + Tag 总长度（≥ TagSize）。</param>
    /// <param name="output">输出缓冲（≥ inputLength - TagSize）。</param>
    /// <returns>明文字节数。</returns>
    /// <exception cref="DecryptionFailedException">GCM 认证失败。</exception>
    public static int DecryptInto(
        string algorithm, byte[] key, byte[] nonce, byte[] aad,
        byte[] input, int inputOffset, int inputLength,
        byte[] output)
    {
        if (inputLength < TagSize)
            throw new CipherFormatException("STFE 分块长度不足（缺 Tag）");

        var cipher = CreateCipher(algorithm);
        cipher.Init(false, new AeadParameters(
            new KeyParameter(NormalizeKey(key, algorithm)), TagBits, nonce, aad));

        var outputLen = cipher.GetOutputSize(inputLength);
        if (output.Length < outputLen)
            throw new ArgumentException($"输出缓冲区过小（需 ≥ {outputLen}）", nameof(output));

        try
        {
            var len = cipher.ProcessBytes(input, inputOffset, inputLength, output, 0);
            cipher.DoFinal(output, len);
        }
        catch (InvalidCipherTextException ex)
        {
            throw new DecryptionFailedException(
                "STFE 分块解密失败：密钥错误 / 密文损坏 / AAD 不匹配", ex);
        }

        return outputLen;
    }

    /// <summary>创建 AeadBlockCipher（每次调用返回新实例，确保线程安全）。</summary>
    private static IAeadBlockCipher CreateCipher(string algorithm) => algorithm.ToUpperInvariant() switch
    {
        "SM4-GCM" => new GcmBlockCipher(new SM4Engine()),
        "AES-256-GCM" => new GcmBlockCipher(new AesEngine()),
        _ => throw new NotSupportedException($"不支持的算法：{algorithm}")
    };
}
