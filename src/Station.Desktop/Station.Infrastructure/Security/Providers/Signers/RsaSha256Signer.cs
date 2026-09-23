using System.Security.Cryptography;
using System.Text;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Infrastructure.Security.Providers.Signers;

/// <summary>
/// RSA-SHA256 签名（非国密备选）。PEM 格式密钥。
/// </summary>
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
}
