using System.Security.Cryptography;

namespace Station.Crypto.Engine.Signers;

/// <summary>
/// RSA-SHA256 签名。
/// 密钥格式：PKCS#8（私钥）/ SPKI（公钥）。
/// </summary>
public sealed class RsaSha256Signer : ISigner
{
    public string Algorithm => CryptoAlgorithm.RsaSha256;

    public string Sign(byte[] data, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(sig);
    }

    public bool Verify(byte[] data, string signatureBase64, string publicKeyPem)
    {
        if (data is null
            || string.IsNullOrWhiteSpace(signatureBase64)
            || string.IsNullOrWhiteSpace(publicKeyPem))
            return false;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var sig = Convert.FromBase64String(signatureBase64);
            return rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch { return false; }
    }
}
