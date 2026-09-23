using System.Security.Cryptography;
using Station.Crypto.Abstractions;
using Station.Crypto.Algorithms;

namespace Station.Crypto.Providers.Signers;

/// <summary>RSA-SHA256 签名。</summary>
public sealed class RsaSha256Signer : ISigner
{
    public string Algorithm => CryptoAlgorithm.RsaSha256;

    public string Sign(byte[] data, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(sig);
    }

    public bool Verify(byte[] data, string signatureBase64, string publicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            var sig = Convert.FromBase64String(signatureBase64);
            return rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch { return false; }
    }

    /// <summary>生成 RSA 密钥对。</summary>
    public static (string PrivatePem, string PublicPem) GenerateKeyPair(int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        var privPem = rsa.ExportPkcs8PrivateKeyPem();
        var pubPem = rsa.ExportSubjectPublicKeyInfoPem();
        return (privPem, pubPem);
    }
}
