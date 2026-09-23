using System.Text;
using Org.BouncyCastle.Asn1.GM;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Station.Domain.Security;

namespace Station.Infrastructure.Security.Providers.Signers;

/// <summary>
/// SM2 签名 + SM3 摘要（国密标准）。
/// 签名算法：SM3 摘要 → SM2 椭圆曲线签名（DSA 模式）。
/// 默认用户 ID："1234567812345678"（GM/T 0003 标准默认值）。
/// 密钥格式：PEM（PKCS#8 私钥 / SubjectPublicKeyInfo 公钥）。
/// </summary>
public sealed class Sm2Sm3Signer : Application.Security.Abstractions.ISigner
{
    /// <summary>SM2 签名标准默认用户标识。</summary>
    private static readonly byte[] DefaultUserId = Encoding.UTF8.GetBytes("1234567812345678");

    public string Algorithm => CryptoAlgorithm.Sm2Sm3;

    public string Sign(byte[] data, string privateKeyPem)
    {
        var priv = LoadPrivateKey(privateKeyPem);
        var signer = new SM2Signer();
        signer.Init(true, new ParametersWithID(new ParametersWithRandom(priv, new SecureRandom()), DefaultUserId));
        signer.BlockUpdate(data, 0, data.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public bool Verify(byte[] data, string signatureBase64, string publicKeyPem)
    {
        try
        {
            var pub = LoadPublicKey(publicKeyPem);
            var signature = Convert.FromBase64String(signatureBase64);
            var signer = new SM2Signer();
            signer.Init(false, new ParametersWithID(pub, DefaultUserId));
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature);
        }
        catch
        {
            return false;
        }
    }

    private static ECPrivateKeyParameters LoadPrivateKey(string pem)
    {
        var reader = new PemReader(new StringReader(pem));
        var obj = reader.ReadObject();
        return obj switch
        {
            ECPrivateKeyParameters p => p,
            AsymmetricCipherKeyPair kp => (ECPrivateKeyParameters)kp.Private,
            _ => throw new InvalidOperationException("无法解析 SM2 私钥 PEM（需 PKCS#8 或 SEC1 格式）")
        };
    }

    private static ECPublicKeyParameters LoadPublicKey(string pem)
    {
        var reader = new PemReader(new StringReader(pem));
        var obj = reader.ReadObject();
        return obj switch
        {
            ECPublicKeyParameters p => p,
            AsymmetricCipherKeyPair kp => (ECPublicKeyParameters)kp.Public,
            _ => throw new InvalidOperationException("无法解析 SM2 公钥 PEM")
        };
    }

    /// <summary>
    /// 生成新的 SM2 密钥对（供内部授权工具使用）。
    /// </summary>
    public static (string PrivatePem, string PublicPem) GenerateKeyPair()
    {
        var domainParams = GMNamedCurves.GetByName("sm2p256v1");
        var ecParams = new ECDomainParameters(
            domainParams.Curve, domainParams.G, domainParams.N, domainParams.H);
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(ecParams, new SecureRandom()));
        var pair = generator.GenerateKeyPair();

        var privPem = PemEncode("PRIVATE KEY", PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded());
        var pubPem = PemEncode("PUBLIC KEY", SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded());
        return (privPem, pubPem);
    }

    private static string PemEncode(string label, byte[] der)
    {
        var base64 = Convert.ToBase64String(der);
        var sb = new StringBuilder();
        sb.AppendLine($"-----BEGIN {label}-----");
        for (var i = 0; i < base64.Length; i += 64)
            sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        sb.AppendLine($"-----END {label}-----");
        return sb.ToString();
    }
}
