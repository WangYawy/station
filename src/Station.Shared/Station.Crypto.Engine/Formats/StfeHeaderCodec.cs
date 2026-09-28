using System.Buffers.Binary;
using Station.Crypto.Formats;

namespace Station.Crypto.Engine.Formats;

/// <summary>
/// STFE 文件头序列化（小端固定）。
///
/// 布局（32 字节）：
///   [0..4)   Magic "STFE"
///   [4]      Version
///   [5]      AlgorithmId
///   [6..10)  KeyVersion (int32 LE)
///   [10..14) ChunkSize  (int32 LE)
///   [14..22) NonceSeed  (8 字节)
///   [22..32) Reserved   (10 字节，全 0)
///
/// ⚠️ 该布局是持久化契约，一旦上线禁止修改。
/// </summary>
internal static class StfeHeaderCodec
{
    private const byte MagicS = (byte)'S';
    private const byte MagicT = (byte)'T';
    private const byte MagicF = (byte)'F';
    private const byte MagicE = (byte)'E';

    /// <summary>序列化文件头（固定 32 字节，小端）。</summary>
    public static byte[] ToBytes(StfeHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.NonceSeed is null || header.NonceSeed.Length != 8)
            throw new CryptoException("STFE 头部 NonceSeed 必须为 8 字节");

        var h = new byte[StfeHeader.HeaderSize];
        h[0] = MagicS;
        h[1] = MagicT;
        h[2] = MagicF;
        h[3] = MagicE;
        h[4] = header.Version;
        h[5] = header.AlgorithmId;
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(6, 4), header.KeyVersion);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(10, 4), header.ChunkSize);
        Buffer.BlockCopy(header.NonceSeed, 0, h, 14, 8);
        // [22..32) 保留，全 0
        return h;
    }

    /// <summary>
    /// 反序列化文件头。
    /// </summary>
    /// <param name="buffer">至少 32 字节的头部数据。</param>
    /// <param name="totalCiphertextSize">文件总长度（含头部），仅用于信息展示。</param>
    /// <exception cref="InvalidDataException">Magic 不匹配或长度不足。</exception>
    public static StfeHeader Parse(ReadOnlySpan<byte> buffer, long totalCiphertextSize)
    {
        if (buffer.Length < StfeHeader.HeaderSize)
            throw new InvalidDataException("STFE 头部不完整");

        if (buffer[0] != MagicS || buffer[1] != MagicT
            || buffer[2] != MagicF || buffer[3] != MagicE)
            throw new InvalidDataException("不是 STFE 文件");

        var version = buffer[4];
        var algoId = buffer[5];
        var keyVersion = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(6, 4));
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(10, 4));
        var nonceSeed = buffer.Slice(14, 8).ToArray();

        return new StfeHeader(version, algoId, keyVersion, chunkSize, nonceSeed, totalCiphertextSize);
    }
}
