using System.Security.Cryptography;
using Station.Crypto.Formats;

namespace Station.Crypto.Engine.Formats;

/// <summary>
/// STFE 分块文件加解密（SM4-GCM / AES-256-GCM）。
///
/// 【并行策略】批量读取 N 个分块 → 并行加密 → 顺序写入。
/// 批次大小动态计算，保证批次总字节数约 32MB。
/// </summary>
public static class StfeFileEncryptor
{
    /// <summary>默认分块大小（4 MB）。</summary>
    public const int DefaultChunkSize = StfeFormat.DefaultChunkSize;

    private const int TargetBatchBytes = 32 * 1024 * 1024;
    private const int MaxBatchSize = 16;
    private const int MinChunkSize = 1024;
    private const int MaxChunkSize = 64 * 1024 * 1024;

    /// <summary>
    /// 加密文件。
    /// </summary>
    /// <param name="sourcePath">源文件路径。</param>
    /// <param name="targetPath">目标文件路径。</param>
    /// <param name="key">
    /// 文件加密密钥（已由 HKDF 派生）。
    /// 长度按算法处理：SM4 取前 16 字节；AES-256 必须 32 字节。
    /// </param>
    /// <param name="keyVersion">主密钥版本号（写入头部）。</param>
    /// <param name="algorithm">算法名（CryptoAlgorithm.Sm4Gcm / AesGcm）。</param>
    /// <param name="chunkSize">分块大小（[1KB, 64MB]）。</param>
    /// <returns>密文总字节数（含头部）。</returns>
    public static async Task<long> EncryptAsync(
        string sourcePath,
        string targetPath,
        byte[] key,
        int keyVersion,
        string algorithm,
        int chunkSize = DefaultChunkSize,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(key);
        ValidateChunkSize(chunkSize);

        // 提前规范化密钥（拒绝 AES 密钥长度错误）
        var normalizedKey = StfeChunkProcessor.NormalizeKey(key, algorithm);

        var algoId = StfeChunkProcessor.AlgorithmIdOf(algorithm);
        var nonceSeed = RandomNumberGenerator.GetBytes(8);

        var header = new StfeHeader(
            Version: 1,
            AlgorithmId: algoId,
            KeyVersion: keyVersion,
            ChunkSize: chunkSize,
            NonceSeed: nonceSeed,
            TotalCiphertextSize: 0);
        var headerBytes = StfeHeaderCodec.ToBytes(header);

        await using var input = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);

        await using var output = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.Asynchronous);

        await output.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        long totalWritten = headerBytes.Length;

        var batchSize = ComputeBatchSize(chunkSize);
        var plainBuf = new byte[chunkSize];
        var chunkIndex = 0u;
        var eof = false;

        while (!eof)
        {
            ct.ThrowIfCancellationRequested();

            // 1) 批量读取
            var batch = new (uint Index, byte[] Data, int Len)[batchSize];
            var batchCount = 0;
            for (var i = 0; i < batchSize; i++)
            {
                var read = await ReadFullAsync(input, plainBuf, ct).ConfigureAwait(false);
                if (read == 0) { eof = true; break; }

                var data = new byte[read];
                Buffer.BlockCopy(plainBuf, 0, data, 0, read);
                batch[batchCount++] = (chunkIndex, data, read);
                chunkIndex++;

                if (read < chunkSize) { eof = true; break; }
            }

            if (batchCount == 0) break;

            // 2) 并行加密
            var results = new (byte[] Nonce, byte[] Cipher, byte[] Tag)[batchCount];
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.For(0, batchCount, parallelOptions, i =>
            {
                var (idx, data, len) = batch[i];
                var nonce = StfeChunkProcessor.BuildNonce(nonceSeed, idx);
                var (cipher, tag) = StfeChunkProcessor.Encrypt(
                    algorithm, normalizedKey, nonce, headerBytes, data, len);
                results[i] = (nonce, cipher, tag);
            });

            // 3) 顺序写入
            foreach (var (nonce, cipher, tag) in results)
            {
                await output.WriteAsync(nonce, ct).ConfigureAwait(false);
                await output.WriteAsync(cipher, ct).ConfigureAwait(false);
                await output.WriteAsync(tag, ct).ConfigureAwait(false);
                totalWritten += nonce.Length + cipher.Length + tag.Length;
            }
        }

        await output.FlushAsync(ct).ConfigureAwait(false);
        return totalWritten;
    }

    /// <summary>
    /// 加密文件（自动从主密钥派生文件加密密钥）。
    /// </summary>
    public static Task<long> EncryptAsync(
        string sourcePath,
        string targetPath,
        IMasterKeyProvider keys,
        string algorithm,
        int chunkSize = DefaultChunkSize,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var version = keys.CurrentVersion;
        var key = keys.DeriveFileEncryptionKey(version);
        return EncryptAsync(sourcePath, targetPath, key, version, algorithm, chunkSize, ct);
    }

    /// <summary>
    /// 解密文件（一次性解密到磁盘）。
    /// </summary>
    /// <param name="sourcePath">STFE 密文文件路径。</param>
    /// <param name="targetPath">输出明文文件路径。</param>
    /// <param name="keyProvider">按版本号返回文件加密密钥。</param>
    /// <param name="algorithm">
    /// 期望的算法名（可选，用于校验头部）。
    /// 传 null / 空字符串表示不校验。
    /// </param>
    /// <returns>明文字节数。</returns>
    public static async Task<long> DecryptAsync(
        string sourcePath,
        string targetPath,
        Func<int, byte[]> keyProvider,
        string algorithm,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(keyProvider);

        await using var input = StfeDecryptStream.Open(
            sourcePath,
            new DelegateMasterKeyProvider(keyProvider));

        if (!string.IsNullOrEmpty(algorithm)
            && !string.Equals(input.Algorithm, algorithm, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"算法不匹配：头部={input.Algorithm}，期望={algorithm}");
        }

        await using var output = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.Asynchronous);

        // Stream.CopyToAsync 返回 Task（void），需手动循环累加明文长度
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long totalWritten = 0;
            int read;
            while ((read = await input
                .ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                .ConfigureAwait(false)) > 0)
            {
                await output
                    .WriteAsync(buffer.AsMemory(0, read), ct)
                    .ConfigureAwait(false);
                totalWritten += read;
            }
            return totalWritten;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 解密文件（自动从主密钥派生文件加密密钥）。
    /// </summary>
    public static Task<long> DecryptAsync(
        string sourcePath,
        string targetPath,
        IMasterKeyProvider keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return DecryptAsync(sourcePath, targetPath, keys.GetKey, algorithm: string.Empty, ct);
    }

    // ============================================================
    // 探测
    // ============================================================

    /// <summary>读取 STFE 文件头（失败返回 null）。</summary>
    public static StfeHeader? TryReadHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < StfeHeader.HeaderSize) return null;

            var header = new byte[StfeHeader.HeaderSize];
            if (fs.Read(header, 0, StfeHeader.HeaderSize) != StfeHeader.HeaderSize)
                return null;

            return StfeHeaderCodec.Parse(header, fs.Length);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>判断文件是否为 STFE 格式。</summary>
    public static bool IsStfe(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 4) return false;

            Span<byte> buf = stackalloc byte[4];
            if (fs.Read(buf) != 4) return false;

            return buf[0] == (byte)'S'
                && buf[1] == (byte)'T'
                && buf[2] == (byte)'F'
                && buf[3] == (byte)'E';
        }
        catch
        {
            return false;
        }
    }

    // ============================================================
    // 内部
    // ============================================================

    private static void ValidateChunkSize(int chunkSize)
    {
        if (chunkSize < MinChunkSize || chunkSize > MaxChunkSize)
            throw new ArgumentOutOfRangeException(
                nameof(chunkSize), $"chunkSize 必须在 [{MinChunkSize}, {MaxChunkSize}] 之间");
    }

    private static int ComputeBatchSize(int chunkSize)
    {
        var n = TargetBatchBytes / chunkSize;
        return Math.Clamp(n, 1, MaxBatchSize);
    }

    private static async Task<int> ReadFullAsync(
        Stream stream, byte[] buffer, CancellationToken ct)
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

/// <summary>
/// 把 Func&lt;int, byte[]&gt; 适配为 IMasterKeyProvider（仅用于文件解密场景）。
///
/// 语义：DeriveFileEncryptionKey(v) 直接返回 _getKey(v)，
///      因为解密场景下调用方传入的已经是派生好的文件密钥。
/// </summary>
internal sealed class DelegateMasterKeyProvider : IMasterKeyProvider
{
    private readonly Func<int, byte[]> _getKey;

    public DelegateMasterKeyProvider(Func<int, byte[]> getKey)
        => _getKey = getKey ?? throw new ArgumentNullException(nameof(getKey));

    public int CurrentVersion => throw new NotSupportedException("仅用于文件解密，无当前版本语义");
    public IReadOnlyList<int> GetVersions() => Array.Empty<int>();
    public byte[] GetKey(int version) => _getKey(version);
    public Task<int> RotateAsync(CancellationToken ct = default)
        => throw new NotSupportedException("仅用于文件解密，不支持轮换");
    public byte[] DeriveBindingKey()
        => throw new NotSupportedException("仅用于文件解密，不支持 Binding 派生");
    public byte[] DeriveFileEncryptionKey(int keyVersion) => _getKey(keyVersion);
}
