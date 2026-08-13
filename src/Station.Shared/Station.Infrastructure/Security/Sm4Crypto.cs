using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Paddings;

namespace Station.Infrastructure.Security;

/// <summary>
/// 国密 SM4：CBC/PKCS7 用于小数据（授权信息、凭据）加解密；
/// CTR（自实现随机访问流）用于大文件缓存加密，支持按偏移解密（预览 Range/断点续传）。
/// 缓存文件格式：[16B nonce][ciphertext...]，明文大小 = 文件大小 - 16。
/// </summary>
public static class Sm4Crypto
{
    public const int NonceBytes = 16;
    public const int BlockSize = 16;

    // ==================== CBC（小数据） ====================

    public static byte[] EncryptCbc(byte[] key, byte[] data)
    {
        var cipher = new PaddedBufferedBlockCipher(new CbcBlockCipher(new SM4Engine()), new Pkcs7Padding());
        var iv = RandomNumberGenerator.GetBytes(NonceBytes);
        cipher.Init(true, new ParametersWithIV(new KeyParameter(key), iv));
        var output = new byte[cipher.GetOutputSize(data.Length)];
        var len = cipher.ProcessBytes(data, 0, data.Length, output, 0);
        len += cipher.DoFinal(output, len);
        var result = new byte[NonceBytes + len];
        iv.CopyTo(result, 0);
        Array.Copy(output, 0, result, NonceBytes, len);
        return result;
    }

    public static byte[] DecryptCbc(byte[] key, byte[] blob)
    {
        if (blob.Length <= NonceBytes)
        {
            throw new InvalidOperationException("SM4 密文长度无效");
        }

        var cipher = new PaddedBufferedBlockCipher(new CbcBlockCipher(new SM4Engine()), new Pkcs7Padding());
        var iv = blob[..NonceBytes];
        cipher.Init(false, new ParametersWithIV(new KeyParameter(key), iv));
        var output = new byte[cipher.GetOutputSize(blob.Length - NonceBytes)];
        var len = cipher.ProcessBytes(blob, NonceBytes, blob.Length - NonceBytes, output, 0);
        len += cipher.DoFinal(output, len);
        return output[..len];
    }

    // ==================== CTR（大文件，随机访问） ====================

    /// <summary>加密文件：src（明文）→ dst（nonce+密文）。</summary>
    public static void EncryptFile(string sourcePath, string destinationPath, byte[] key)
    {
        using var input = File.OpenRead(sourcePath);
        using var output = File.Create(destinationPath);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        output.Write(nonce, 0, NonceBytes);
        var engine = new SM4Engine();
        engine.Init(true, new KeyParameter(key));
        var counter = (byte[])nonce.Clone();
        var keystream = new byte[BlockSize];
        var ksOffset = BlockSize;
        var buffer = new byte[64 * 1024];
        var outBuffer = new byte[64 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (ksOffset >= BlockSize)
                {
                    engine.ProcessBlock(counter, 0, keystream, 0);
                    IncrementCounter(counter);
                    ksOffset = 0;
                }

                outBuffer[i] = (byte)(buffer[i] ^ keystream[ksOffset++]);
            }

            output.Write(outBuffer, 0, read);
        }
    }

    /// <summary>原地加密（临时文件 + 替换）。</summary>
    public static void EncryptInPlace(string path, byte[] key)
    {
        var temp = path + ".sm4.tmp";
        EncryptFile(path, temp, key);
        File.Move(temp, path, true);
    }

    /// <summary>打开解密读取流：支持从明文偏移开始（16 字节对齐块，Range 兼容）。</summary>
    public static Sm4DecryptStream CreateDecryptReader(string encryptedPath, byte[] key, long plaintextOffset = 0) =>
        new(File.Open(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read), key, plaintextOffset);

    internal static void IncrementCounter(byte[] counter)
    {
        for (var i = counter.Length - 1; i >= 0; i--)
        {
            counter[i]++;
            if (counter[i] != 0)
            {
                break;
            }
        }
    }

    internal static void AddBlockIndex(byte[] counter, long blockIndex)
    {
        var carry = blockIndex;
        for (var i = counter.Length - 1; i >= 0 && carry > 0; i--)
        {
            var sum = counter[i] + (byte)(carry & 0xFF);
            counter[i] = (byte)sum;
            carry = (carry >> 8) + (sum >> 8);
        }
    }
}

/// <summary>SM4-CTR 解密读取流：按明文偏移定位（块对齐），流式 XOR 解密。</summary>
public sealed class Sm4DecryptStream : Stream
{
    private readonly Stream _inner;
    private readonly SM4Engine _engine = new();
    private readonly byte[] _counter = new byte[Sm4Crypto.NonceBytes];
    private readonly byte[] _keystream = new byte[Sm4Crypto.BlockSize];
    private int _ksOffset = Sm4Crypto.BlockSize;
    private long _position;

    public Sm4DecryptStream(Stream encryptedStream, byte[] key, long plaintextOffset = 0)
    {
        if (!encryptedStream.CanSeek)
        {
            throw new ArgumentException("SM4 解密流需要可定位的输入流", nameof(encryptedStream));
        }

        _inner = encryptedStream;
        // CTR 密钥流 = E(counter)，与加解密方向无关，统一使用加密方向生成密钥流
        _engine.Init(true, new KeyParameter(key));
        var nonce = new byte[Sm4Crypto.NonceBytes];
        _inner.ReadExactly(nonce, 0, Sm4Crypto.NonceBytes);
        nonce.CopyTo(_counter, 0);
        var blockIndex = plaintextOffset / Sm4Crypto.BlockSize;
        var skip = (int)(plaintextOffset % Sm4Crypto.BlockSize);
        Sm4Crypto.AddBlockIndex(_counter, blockIndex);
        _inner.Position = Sm4Crypto.NonceBytes + blockIndex * Sm4Crypto.BlockSize;
        _position = plaintextOffset;
        if (skip > 0)
        {
            var discard = new byte[skip];
            ReadCore(discard, 0, skip);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer, offset, count);

    private int ReadCore(byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            if (_ksOffset >= Sm4Crypto.BlockSize)
            {
                _engine.ProcessBlock(_counter, 0, _keystream, 0);
                Sm4Crypto.IncrementCounter(_counter);
                _ksOffset = 0;
            }

            var read = _inner.Read(buffer, offset + total, count - total);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                buffer[offset + total + i] ^= _keystream[_ksOffset];
                _ksOffset++;
                if (_ksOffset >= Sm4Crypto.BlockSize)
                {
                    _engine.ProcessBlock(_counter, 0, _keystream, 0);
                    Sm4Crypto.IncrementCounter(_counter);
                    _ksOffset = 0;
                }
            }

            total += read;
        }

        _position += total;
        return total;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length - Sm4Crypto.NonceBytes;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
