namespace Station.Application.Security.Abstractions;

/// <summary>签名器抽象。PEM 格式密钥（PKCS#8 / SEC1）。</summary>
public interface ISigner
{
    /// <summary>算法标识。</summary>
    string Algorithm { get; }

    /// <summary>对数据签名，返回 Base64 签名值。</summary>
    string Sign(byte[] data, string privateKeyPem);

    /// <summary>验签。</summary>
    bool Verify(byte[] data, string signatureBase64, string publicKeyPem);
}
