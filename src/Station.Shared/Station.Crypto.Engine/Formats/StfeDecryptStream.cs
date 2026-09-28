using System.Buffers;
using Station.Crypto;
using Station.Crypto.Formats;

namespace Station.Crypto.Engine.Formats;

/// <summary>
/// STFE 流式解密（顺序读，不支持 Seek）。
///
/// 【内存】O(chunkSize) = 4MB（默认）。
/// 【线程安全】不支持多线程并发读（Stream 约定）。
/// 【生命周期】Dispose 时释放底层流。
///
/// 用法：
/// <code>
/// await using var stream = StfeDecryptStream.Open("file.stfe", keys);
/// await stream.CopyToAsync(output);
/// </code>
/// </summary>
public sealed class StfeDecryptStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _ownsInner;
    private readonly string _algorithm;
    private readonly byte[] _key;
    private readonly byte[] _headerBytes;
    private readonly int _chunkSize;
    private readonly byte[] _readBuffer;      // [Nonce | Ciphertext | Tag]
    private readonly byte[] _plainBuffer;     // 明文书出缓冲（预分配 chunkSize）
    private readonly int _maxReadSize;

    private int _plainLength;
    private int _plainOffset;
    private bool _eof;
    private bool _disposed;

    /// <summary>
    /// 打开 STFE 文件用于流式解密。
    /// </summary>
    /// <param name="path">STFE 文件路径。</param>
    /// <param name="keys">主密钥提供者（按头部 KeyVersion 派生文件密钥）。</param>
    public static StfeDecryptStream Open(string path, IMasterKeyProvider keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(keys);

        var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        try
        {
            return new StfeDecryptStream(fs, ownsInner: true, keys);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>打开流式解密（不拥有内部流的释放权）。</summary>
    public static StfeDecryptStream Open(Stream inner, IMasterKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(keys);
        return new StfeDecryptStream(inner, ownsInner: false, keys);
    }

    private StfeDecryptStream(Stream inner, bool ownsInner, IMasterKeyProvider keys)
    {
        _inner = inner;
        _ownsInner = ownsInner;

        // 1) 读头部
        var headerBytes = new byte[StfeHeader.HeaderSize];
        if (ReadFull(inner, headerBytes) != headerBytes.Length)
            throw new InvalidDataException("STFE 头部读取失败");

        var header = StfeHeaderCodec.Parse(headerBytes, 0);
        if (header.Version != 1)
            throw new InvalidDataException($"不支持的 STFE 版本：{header.Version}");

        _algorithm = header.AlgorithmName;
        if (_algorithm == "Unknown")
            throw new InvalidDataException($"不支持的算法 ID：0x{header.AlgorithmId:X2}");

        if (header.ChunkSize <= 0 || header.ChunkSize > 64 * 1024 * 1024)
            throw new InvalidDataException($"非法的 chunkSize：{header.ChunkSize}");

        _chunkSize = header.ChunkSize;
        _key = keys.DeriveFileEncryptionKey(header.KeyVersion);
        _headerBytes = headerBytes;
        _maxReadSize = StfeChunkProcessor.NonceSize + _chunkSize + StfeChunkProcessor.TagSize;
        _readBuffer = new byte[_maxReadSize];
        _plainBuffer = new byte[_chunkSize];
    }

    /// <summary>文件算法名（"SM4-GCM" / "AES-256-GCM"）。</summary>
    public string Algorithm => _algorithm;

    /// <summary>文件头 KeyVersion。</summary>
    public int KeyVersion => BitConverter.ToInt32(_headerBytes, 6);

    /// <summary>文件头 ChunkSize。</summary>
    public int ChunkSize => _chunkSize;

    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException("STFE 流不支持 Length（顺序流）");
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    // ============================================================
    // 读
    // ============================================================

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length == 0) return 0;
        if (_plainOffset < _plainLength) return CopyFromPlain(buffer);
        if (_eof) return 0;

        if (!ReadAndDecryptNextChunk()) return 0;
        return CopyFromPlain(buffer);
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length == 0) return ValueTask.FromResult(0);
        if (_plainOffset < _plainLength) return ValueTask.FromResult(CopyFromPlain(buffer.Span));
        if (_eof) return ValueTask.FromResult(0);

        return ReadAsyncCore(buffer, ct);
    }

    private async ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancellationToken ct)
    {
        var ok = await ReadAndDecryptNextChunkAsync(ct).ConfigureAwait(false);
        if (!ok) return 0;
        return CopyFromPlain(buffer.Span);
    }

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    private int CopyFromPlain(Span<byte> buffer)
    {
        var available = _plainLength - _plainOffset;
        var toCopy = Math.Min(available, buffer.Length);
        _plainBuffer.AsSpan(_plainOffset, toCopy).CopyTo(buffer);
        _plainOffset += toCopy;
        return toCopy;
    }

    // ============================================================
    // 单块解密
    // ============================================================

    private bool ReadAndDecryptNextChunk()
    {
        var read = ReadFull(_inner, _readBuffer);
        return ProcessChunk(read);
    }

    private async ValueTask<bool> ReadAndDecryptNextChunkAsync(CancellationToken ct)
    {
        var read = await ReadFullAsync(_inner, _readBuffer, ct).ConfigureAwait(false);
        return ProcessChunk(read);
    }

    private bool ProcessChunk(int read)
    {
        if (read == 0) { _eof = true; return false; }
        if (read < StfeChunkProcessor.NonceSize + StfeChunkProcessor.TagSize)
            throw new InvalidDataException("STFE 分块长度不足（缺 Nonce 或 Tag）");

        var nonce = _readBuffer.AsSpan(0, StfeChunkProcessor.NonceSize).ToArray();
        var cipherAndTagLen = read - StfeChunkProcessor.NonceSize;

        _plainLength = StfeChunkProcessor.DecryptInto(
            _algorithm, _key, nonce, _headerBytes,
            _readBuffer, StfeChunkProcessor.NonceSize, cipherAndTagLen,
            _plainBuffer);
        _plainOffset = 0;
        return true;
    }

    // ============================================================
    // 内部工具
    // ============================================================

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static async ValueTask<int> ReadFullAsync(
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

    // ============================================================
    // 不支持的操作
    // ============================================================

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException("STFE 流不支持 Seek");

    public override void SetLength(long value)
        => throw new NotSupportedException("STFE 流不支持 SetLength");

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("STFE 流不支持 Write");

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing && _ownsInner)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsInner)
            await _inner.DisposeAsync().ConfigureAwait(false);

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
