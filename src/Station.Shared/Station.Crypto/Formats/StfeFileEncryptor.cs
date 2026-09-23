using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Station.Crypto.Formats;

/// <summary>STFE 分块文件加解密（SM4-GCM / AES-256-GCM）。</summary>
public static class StfeFileEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    public const int DefaultChunkSize = 4 * 1024 * 1024;

    /// <summary>加密文件。返回密文总字节数。</summary>
    public static async Task<long> EncryptAsync(
        string sourcePath,
        string targetPath,
        byte[] key,
        int keyVersion,
        string algorithm,
        int chunkSize = DefaultChunkSize,
        CancellationToken ct = default)
    {
        var (algoByte, engineFactory) = ResolveAlgorithm(algorithm);
        var nonceSeed = RandomNumberGenerator.GetBytes(8);

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, chunkSize, useAsync: true);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write,
            FileShare.None, chunkSize, useAsync: true);

        var header = new StfeHeader(1, algoByte, keyVersion, chunkSize, nonceSeed, 0);
        var headerBytes = header.ToBytes();
        await output.WriteAsync(headerBytes, ct).ConfigureAwait(false);

        var plainBuf = new byte[chunkSize];
        var chunkIndex = 0u;
        long totalWritten = headerBytes.Length;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await ReadFullAsync(input, plainBuf, ct).ConfigureAwait(false);
            if (read == 0) break;

            var nonce = BuildNonce(nonceSeed, chunkIndex);
            var (ciphertext, tag) = EncryptChunk(engineFactory, key, nonce, headerBytes, plainBuf, read);

            await output.WriteAsync(nonce, ct).ConfigureAwait(false);
            await output.WriteAsync(ciphertext, ct).ConfigureAwait(false);
            await output.WriteAsync(tag, ct).ConfigureAwait(false);

            totalWritten += nonce.Length + ciphertext.Length + tag.Length;
            chunkIndex++;

            if (read < chunkSize) break;
        }

        await output.FlushAsync(ct).ConfigureAwait(false);
        return totalWritten;
    }

    /// <summary>解密文件。返回明文字节数。</summary>
    public static async Task<long> DecryptAsync(
        string sourcePath,
        string targetPath,
        Func<int, byte[]> keyProvider,
        string algorithm,
        CancellationToken ct = default)
    {
        var (_, engineFactory) = ResolveAlgorithm(algorithm);

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, useAsync: true);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);

        var headerBytes = new byte[StfeHeader.HeaderSize];
        if (await ReadFullAsync(input, headerBytes, ct).ConfigureAwait(false) != StfeHeader.HeaderSize)
            throw new InvalidDataException("STFE 头部读取失败");

        var header = StfeHeader.Parse(headerBytes, input.Length);
        var key = keyProvider(header.KeyVersion);

        var buf = new byte[NonceSize + header.ChunkSize + TagSize];
        long totalWritten = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await ReadFullAsync(input, buf, ct).ConfigureAwait(false);
            if (read == 0) break;
            if (read < NonceSize + TagSize) throw new InvalidDataException("STFE 分块长度不足");

            var nonce = buf.AsSpan(0, NonceSize).ToArray();
            var cipherLen = read - NonceSize - TagSize;
            var ciphertext = buf.AsSpan(NonceSize, cipherLen).ToArray();
            var tag = buf.AsSpan(NonceSize + cipherLen, TagSize).ToArray();

            var plain = DecryptChunk(engineFactory, key, nonce, headerBytes, ciphertext, tag);
            await output.WriteAsync(plain, ct).ConfigureAwait(false);
            totalWritten += plain.Length;

            if (cipherLen < header.ChunkSize) break;
        }

        await output.FlushAsync(ct).ConfigureAwait(false);
        return totalWritten;
    }

    /// <summary>探测 STFE 文件头。</summary>
    public static StfeHeader? TryReadHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < StfeHeader.HeaderSize) return null;
            var header = new byte[StfeHeader.HeaderSize];
            if (fs.Read(header, 0, StfeHeader.HeaderSize) != StfeHeader.HeaderSize) return null;
            return StfeHeader.Parse(header, fs.Length);
        }
        catch { return null; }
    }

    /// <summary>判断文件是否为 STFE 格式。</summary>
    public static bool IsStfe(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 4) return false;
            var buf = new byte[4];
            return fs.Read(buf, 0, 4) == 4
                && buf[0] == StfeHeader.Magic[0]
                && buf[1] == StfeHeader.Magic[1]
                && buf[2] == StfeHeader.Magic[2]
                && buf[3] == StfeHeader.Magic[3];
        }
        catch { return false; }
    }

    // ============ 内部 ============

    private static (byte algoByte, Func<IBlockCipher, IAeadBlockCipher> factory) ResolveAlgorithm(string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "SM4-GCM" => (StfeHeader.AlgoSm4Gcm, _ => new GcmBlockCipher(new SM4Engine())),
            "AES-256-GCM" => (StfeHeader.AlgoAesGcm, _ => new GcmBlockCipher(new AesEngine())),
            _ => throw new NotSupportedException($"不支持的文件加密算法：{algorithm}")
        };
    }

    private static byte[] BuildNonce(byte[] seed, uint chunkIndex)
    {
        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(seed, 0, nonce, 0, 8);
        BitConverter.TryWriteBytes(nonce.AsSpan(8, 4), chunkIndex);
        return nonce;
    }

    private static (byte[] cipher, byte[] tag) EncryptChunk(
        Func<IBlockCipher, IAeadBlockCipher> factory,
        byte[] key, byte[] nonce, byte[] header, byte[] plain, int plainLen)
    {
        var cipher = factory(null!);
        cipher.Init(true, new AeadParameters(new KeyParameter(PadKey(key)), TagSize * 8, nonce, header));

        var output = new byte[cipher.GetOutputSize(plainLen)];
        var len = cipher.ProcessBytes(plain, 0, plainLen, output, 0);
        cipher.DoFinal(output, len);

        var ciphertext = new byte[output.Length - TagSize];
        var tag = new byte[TagSize];
        Buffer.BlockCopy(output, 0, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(output, ciphertext.Length, tag, 0, TagSize);
        return (ciphertext, tag);
    }

    private static byte[] DecryptChunk(
        Func<IBlockCipher, IAeadBlockCipher> factory,
        byte[] key, byte[] nonce, byte[] header, byte[] ciphertext, byte[] tag)
    {
        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        var cipher = factory(null!);
        cipher.Init(false, new AeadParameters(new KeyParameter(PadKey(key)), TagSize * 8, nonce, header));

        var output = new byte[cipher.GetOutputSize(combined.Length)];
        var len = cipher.ProcessBytes(combined, 0, combined.Length, output, 0);
        cipher.DoFinal(output, len);
        return output;
    }

    private static byte[] PadKey(byte[] key) => key.Length == 16 ? key : key[..16];

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
