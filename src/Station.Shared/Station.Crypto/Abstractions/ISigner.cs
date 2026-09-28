namespace Station.Crypto;

/// <summary>
/// 签名器抽象。
/// 密钥格式：PEM（PKCS#8 / SEC1 / SPKI）。
/// </summary>
public interface ISigner
{
    /// <summary>算法标识（CryptoAlgorithm 常量）。</summary>
    string Algorithm { get; }

    /// <summary>
    /// 对数据签名，返回 Base64 签名值。
    /// 注意：SM2-SM3 内部已含 SM3 摘要，传入的应是原始数据，不要预哈希。
    /// </summary>
    string Sign(byte[] data, string privateKeyPem);

    /// <summary>
    /// 验签。
    /// 失败（含格式异常）返回 false，不抛异常。
    /// </summary>
    bool Verify(byte[] data, string signatureBase64, string publicKeyPem);
}
