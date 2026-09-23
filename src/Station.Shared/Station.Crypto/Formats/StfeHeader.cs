namespace Station.Crypto.Formats;

/// <summary>STFE 文件头信息。</summary>
public sealed record StfeHeader(
    byte Version,
    byte AlgorithmId,
    int KeyVersion,
    int ChunkSize,
    byte[] NonceSeed,
    long TotalCiphertextSize)
{
    public const int HeaderSize = 32;
    public static readonly byte[] Magic = { (byte)'S', (byte)'T', (byte)'F', (byte)'E' };
    public const byte AlgoSm4Gcm = 0x01;
    public const byte AlgoAesGcm = 0x02;

    public string AlgorithmName => AlgorithmId switch
    {
        AlgoSm4Gcm => "SM4-GCM",
        AlgoAesGcm => "AES-256-GCM",
        _ => "Unknown"
    };

    public byte[] ToBytes()
    {
        var h = new byte[HeaderSize];
        Buffer.BlockCopy(Magic, 0, h, 0, 4);
        h[4] = Version;
        h[5] = AlgorithmId;
        BitConverter.TryWriteBytes(h.AsSpan(6, 4), KeyVersion);
        BitConverter.TryWriteBytes(h.AsSpan(10, 4), ChunkSize);
        Buffer.BlockCopy(NonceSeed, 0, h, 14, 8);
        return h;
    }

    public static StfeHeader Parse(byte[] buffer, long totalCiphertextSize)
    {
        if (buffer.Length < HeaderSize) throw new InvalidDataException("STFE 头部不完整");
        if (buffer[0] != Magic[0] || buffer[1] != Magic[1]
            || buffer[2] != Magic[2] || buffer[3] != Magic[3])
            throw new InvalidDataException("不是 STFE 文件");

        var version = buffer[4];
        var algo = buffer[5];
        var keyVersion = BitConverter.ToInt32(buffer, 6);
        var chunkSize = BitConverter.ToInt32(buffer, 10);
        var nonceSeed = new byte[8];
        Buffer.BlockCopy(buffer, 14, nonceSeed, 0, 8);
        return new StfeHeader(version, algo, keyVersion, chunkSize, nonceSeed, totalCiphertextSize);
    }
}
