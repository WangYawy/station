using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;

/// <summary>
/// 分块文件加密服务（STFE 格式）。
/// 
/// 【格式】
///   Header(32B): Magic "STFE" | Ver(1) | Algo(1) | KeyVer(4) | ChunkSize(4) | NonceSeed(8) | Reserved(10)
///   Chunk:       Nonce(12) | Ciphertext(N) | Tag(16)
/// 
/// 【密钥派生】
///   主密钥 → HKDF-SM3(info="station:file-encryption:v1") → 文件加密密钥（32B）
/// 
/// 【算法】
///   SM4-GCM / AES-256-GCM，由 CryptoUsage.FileEncryption 策略决定。
/// </summary>
public sealed class FileEncryptionService : IFileEncryptionService
{
    private const int HeaderSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const byte Version = 1;
    private const byte AlgoSm4Gcm = 0x01;
    private const byte AlgoAesGcm = 0x02;
    private const int DefaultChunkSize = 4 * 1024 * 1024;   // 4MB
    private static readonly byte[] Magic = { (byte)'S', (byte)'T', (byte)'F', (byte)'E' };
    private static readonly byte[] HkdfInfo = System.Text.Encoding.UTF8.GetBytes("station:file-encryption:v1");

    private readonly ICryptoPolicyService _policy;
    private readonly IMasterKeyProvider _keyProvider;
    private readonly ILogger<FileEncryptionService> _logger;

    public FileEncryptionService(
        ICryptoPolicyService policy,
        IMasterKeyProvider keyProvider,
        ILogger<FileEncryptionService> logger)
    {
        _policy = policy;
        _keyProvider = keyProvider;
        _logger = logger;
    }

    // =========================================================
    // 加密
    // =========================================================

    public async Task<long> EncryptAsync(
        string sourcePath, string targetPath, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileEncryption, ct).ConfigureAwait(false);
        var (algoByte, engineFactory) = ResolveAlgorithm(policy.Algorithm);

        var keyVersion = _keyProvider.CurrentVersion;
        var masterKey = _keyProvider.GetKey(keyVersion);
        var fileKey = DeriveFileKey(masterKey);

        var nonceSeed = RandomNumberGenerator.GetBytes(8);
        var chunkSize = DefaultChunkSize;

        await using var input = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: chunkSize, useAsync: true);

        await using var output = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: chunkSize, useAsync: true);

        // ---- 写 Header ----
        var header = BuildHeader(algoByte, keyVersion, chunkSize, nonceSeed);
        await output.WriteAsync(header, ct).ConfigureAwait(false);

        // ---- 分块加密 ----
        var plainBuf = new byte[chunkSize];
        var chunkIndex = 0u;
        long totalWritten = header.Length;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await ReadFullAsync(input, plainBuf, ct).ConfigureAwait(false);
            if (read == 0) break;

            var nonce = BuildNonce(nonceSeed, chunkIndex);
            var (ciphertext, tag) = EncryptChunk(engineFactory, fileKey, nonce, header, plainBuf, read);

            // 写 [Nonce | Cipher | Tag]
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

    public async Task<long> EncryptInPlaceAsync(string filePath, CancellationToken ct = default)
    {
        var tempPath = filePath + ".stfe.tmp";
        try
        {
            var size = await EncryptAsync(filePath, tempPath, ct).ConfigureAwait(false);

            if (OperatingSystem.IsWindows() && File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, null);
            }
            else
            {
                File.Move(tempPath, filePath, overwrite: true);
            }

            return size;
        }
        catch
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    // =========================================================
    // 解密
    // =========================================================

    public Stream CreateDecryptStream(string encryptedFilePath)
    {
        return new DecryptStream(encryptedFilePath, _keyProvider, _policy, _logger);
    }

    // =========================================================
    // 探测
    // =========================================================

    public bool IsEncrypted(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < HeaderSize) return false;
            var buf = new byte[4];
            var n = fs.Read(buf, 0, 4);
            return n == 4 && buf[0] == Magic[0] && buf[1] == Magic[1]
                        && buf[2] == Magic[2] && buf[3] == Magic[3];
        }
        catch { return false; }
    }

    public FileEncryptionHeader? TryReadHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < HeaderSize) return null;
            var header = new byte[HeaderSize];
            if (fs.Read(header, 0, HeaderSize) != HeaderSize) return null;
            if (header[0] != Magic[0] || header[1] != Magic[1]
                || header[2] != Magic[2] || header[3] != Magic[3]) return null;

            var version = header[4];
            var algo = header[5] switch { AlgoSm4Gcm => "SM4-GCM", AlgoAesGcm => "AES-256-GCM", _ => "Unknown" };
            var keyVersion = BitConverter.ToInt32(header, 6);
            var chunkSize = BitConverter.ToInt32(header, 10);
            return new FileEncryptionHeader(version, algo, keyVersion, chunkSize, fs.Length);
        }
        catch { return null; }
    }

    // =========================================================
    // 内部
    // =========================================================

    private static (byte algoByte, Func<IAeadBlockCipher> factory) ResolveAlgorithm(string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "SM4-GCM" => (AlgoSm4Gcm, () => new GcmBlockCipher(new SM4Engine())),
            "AES-256-GCM" => (AlgoAesGcm, () => new GcmBlockCipher(new AesEngine())),
            _ => throw new NotSupportedException($"不支持的文件加密算法：{algorithm}")
        };
    }

    private static byte[] DeriveFileKey(byte[] masterKey)
    {
        var hkdf = new HkdfBytesGenerator(new SM3Digest());
        // ★ 修复：显式 (byte[]?)null，避免命名参数歧义
        hkdf.Init(new HkdfParameters(masterKey, (byte[]?)null, HkdfInfo));
        var derived = new byte[KeySize];
        hkdf.GenerateBytes(derived, 0, KeySize);
        return derived;
    }

    private static byte[] BuildHeader(byte algo, int keyVersion, int chunkSize, byte[] nonceSeed)
    {
        var h = new byte[HeaderSize];
        Buffer.BlockCopy(Magic, 0, h, 0, 4);
        h[4] = Version;
        h[5] = algo;
        BitConverter.TryWriteBytes(h.AsSpan(6, 4), keyVersion);
        BitConverter.TryWriteBytes(h.AsSpan(10, 4), chunkSize);
        Buffer.BlockCopy(nonceSeed, 0, h, 14, 8);
        return h;
    }

    private static byte[] BuildNonce(byte[] seed, uint chunkIndex)
    {
        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(seed, 0, nonce, 0, 8);
        BitConverter.TryWriteBytes(nonce.AsSpan(8, 4), chunkIndex);
        return nonce;
    }

    private static (byte[] cipher, byte[] tag) EncryptChunk(
        Func<IAeadBlockCipher> factory, byte[] key, byte[] nonce, byte[] header,
        byte[] plain, int plainLen)
    {
        var cipher = factory();
        cipher.Init(true, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce, header));

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
        Func<IAeadBlockCipher> factory, byte[] key, byte[] nonce, byte[] header,
        byte[] ciphertext, byte[] tag)
    {
        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        var cipher = factory();
        cipher.Init(false, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce, header));

        var output = new byte[cipher.GetOutputSize(combined.Length)];
        var len = cipher.ProcessBytes(combined, 0, combined.Length, output, 0);
        cipher.DoFinal(output, len);
        return output;
    }

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

    // =========================================================
    // 解密流
    // =========================================================

    private sealed class DecryptStream : Stream
    {
        private readonly string _path;
        private readonly IMasterKeyProvider _keyProvider;
        private readonly ICryptoPolicyService _policy;
        private readonly ILogger _logger;
        private MemoryStream? _decrypted;
        private long _position;

        public DecryptStream(string path, IMasterKeyProvider kp, ICryptoPolicyService policy, ILogger logger)
        {
            _path = path;
            _keyProvider = kp;
            _policy = policy;
            _logger = logger;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => EnsureDecrypted().Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        private MemoryStream EnsureDecrypted()
        {
            if (_decrypted is not null) return _decrypted;

            var policy = _policy.GetAsync(CryptoUsage.FileEncryption).GetAwaiter().GetResult();
            var (_, factory) = ResolveAlgorithm(policy.Algorithm);

            using var fs = File.OpenRead(_path);
            var header = new byte[HeaderSize];
            if (fs.Read(header, 0, HeaderSize) != HeaderSize)
                throw new InvalidDataException("STFE 头部不完整");
            if (header[0] != Magic[0] || header[1] != Magic[1]
                || header[2] != Magic[2] || header[3] != Magic[3])
                throw new InvalidDataException("不是 STFE 文件");

            var keyVersion = BitConverter.ToInt32(header, 6);
            var chunkSize = BitConverter.ToInt32(header, 10);
            var nonceSeed = new byte[8];
            Buffer.BlockCopy(header, 14, nonceSeed, 0, 8);

            var masterKey = _keyProvider.GetKey(keyVersion);
            var fileKey = DeriveFileKey(masterKey);

            var output = new MemoryStream();
            var buf = new byte[NonceSize + chunkSize + TagSize];

            while (true)
            {
                var read = ReadFullAsyncSync(fs, buf);
                if (read == 0) break;
                if (read < NonceSize + TagSize)
                    throw new InvalidDataException("STFE 分块长度不足");

                var nonce = new byte[NonceSize];
                Buffer.BlockCopy(buf, 0, nonce, 0, NonceSize);

                var cipherLen = read - NonceSize - TagSize;
                var ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(buf, NonceSize, ciphertext, 0, cipherLen);

                var tag = new byte[TagSize];
                Buffer.BlockCopy(buf, NonceSize + cipherLen, tag, 0, TagSize);

                var plain = DecryptChunk(factory, fileKey, nonce, header, ciphertext, tag);
                output.Write(plain, 0, plain.Length);

                if (cipherLen < chunkSize) break;
            }

            output.Position = 0;
            _decrypted = output;
            return output;
        }

        private static int ReadFullAsyncSync(Stream s, byte[] buf)
        {
            var total = 0;
            while (total < buf.Length)
            {
                var n = s.Read(buf, total, buf.Length - total);
                if (n == 0) break;
                total += n;
            }
            return total;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var ms = EnsureDecrypted();
            var n = ms.Read(buffer, offset, count);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
