namespace Station.Crypto.Formats;

/// <summary>
/// STFE 文件头信息（纯数据，序列化/反序列化在 Engine 层）。
///
/// 布局（32 字节，全部小端）：
///   [0..4)   Magic "STFE"
///   [4]      Version        (当前 1)
///   [5]      AlgorithmId    (0x01=SM4-GCM, 0x02=AES-256-GCM)
///   [6..10)  KeyVersion     (int32 LE)
///   [10..14) ChunkSize      (int32 LE)
///   [14..22) NonceSeed      (8 字节)
///   [22..32) Reserved       (10 字节，全 0)
/// </summary>
public sealed record StfeHeader(
    byte Version,
    byte AlgorithmId,
    int KeyVersion,
    int ChunkSize,
    byte[] NonceSeed,
    long TotalCiphertextSize)
{
    /// <summary>文件头字节数。</summary>
    public const int HeaderSize = 32;

    /// <summary>SM4-GCM 算法 ID。</summary>
    public const byte AlgoSm4Gcm = 0x01;

    /// <summary>AES-256-GCM 算法 ID。</summary>
    public const byte AlgoAesGcm = 0x02;

    /// <summary>算法名（映射到 CryptoAlgorithm 常量值）。</summary>
    public string AlgorithmName => AlgorithmId switch
    {
        AlgoSm4Gcm => "SM4-GCM",
        AlgoAesGcm => "AES-256-GCM",
        _ => "Unknown"
    };
}
