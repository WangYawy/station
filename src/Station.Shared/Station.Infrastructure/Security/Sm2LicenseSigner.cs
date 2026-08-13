using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1.GM;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Station.Infrastructure.Security;

/// <summary>
/// SM2 授权签名（国密非对称）：内部工具私钥签名，采集站内置公钥验签。
/// ID 使用国密默认 "1234567812345678"。
/// </summary>
public static class Sm2LicenseSigner
{
    private const string PrivatePrefix = "SM2-PRIVATE:";
    private const string PublicPrefix = "SM2-PUBLIC:";
    private const string Id = "1234567812345678";
    private static readonly X9ECParameters Curve = ECNamedCurveTable.GetByOid(GMObjectIdentifiers.sm2p256v1);
    private static readonly ECDomainParameters Domain = new(Curve.Curve, Curve.G, Curve.N, Curve.H);

    /// <summary>生成 SM2 密钥对，返回 (私钥PEM, 公钥PEM)。</summary>
    public static (string PrivatePem, string PublicPem) CreateKeyPair()
    {
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(Domain, new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        return (ToPem(pair.Private), ToPem(pair.Public));
    }

    public static string Sign(string privateKeyPem, string text)
    {
        var privateKey = ReadPrivateKey(privateKeyPem);
        var signer = new SM2Signer();
        signer.Init(true, new ParametersWithID(privateKey, Encoding.UTF8.GetBytes(Id)));
        var data = Encoding.UTF8.GetBytes(text);
        signer.BlockUpdate(data, 0, data.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public static bool Verify(string publicKeyPem, string text, string signature)
    {
        try
        {
            var publicKey = ReadPublicKey(publicKeyPem);
            var signer = new SM2Signer();
            signer.Init(false, new ParametersWithID(publicKey, Encoding.UTF8.GetBytes(Id)));
            var data = Encoding.UTF8.GetBytes(text);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(Convert.FromBase64String(signature));
        }
        catch
        {
            return false;
        }
    }

    private static ECPrivateKeyParameters ReadPrivateKey(string pem)
    {
        if (!pem.StartsWith(PrivatePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("私钥格式无效");
        }

        var key = PrivateKeyFactory.CreateKey(Convert.FromBase64String(pem[PrivatePrefix.Length..]));
        return key as ECPrivateKeyParameters ?? throw new InvalidOperationException("私钥格式无效");
    }

    private static ECPublicKeyParameters ReadPublicKey(string pem)
    {
        if (!pem.StartsWith(PublicPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("公钥格式无效");
        }

        var key = PublicKeyFactory.CreateKey(Convert.FromBase64String(pem[PublicPrefix.Length..]));
        return key as ECPublicKeyParameters ?? throw new InvalidOperationException("公钥格式无效");
    }

    private static string ToPem(AsymmetricKeyParameter key)
    {
        return key switch
        {
            ECPrivateKeyParameters => PrivatePrefix + Convert.ToBase64String(
                PrivateKeyInfoFactory.CreatePrivateKeyInfo(key).GetDerEncoded()),
            ECPublicKeyParameters => PublicPrefix + Convert.ToBase64String(
                SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key).GetDerEncoded()),
            _ => throw new InvalidOperationException("不支持的密钥类型")
        };
    }
}
