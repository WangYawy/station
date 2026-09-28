using System.Text;
using Org.BouncyCastle.Asn1.GM;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Station.Crypto.Engine.KeyGen;

/// <summary>SM2 密钥生成器（sm2p256v1 曲线）。</summary>
public static class Sm2KeyGenerator
{
    /// <summary>
    /// 生成 SM2 密钥对。
    /// </summary>
    /// <returns>(私钥 PEM, 公钥 PEM) — PKCS#8 / SPKI 格式。</returns>
    public static (string PrivatePem, string PublicPem) Generate()
    {
        var domainParams = GMNamedCurves.GetByName("sm2p256v1")
            ?? throw new InvalidOperationException("未找到 sm2p256v1 曲线");

        var ecParams = new ECDomainParameters(
            domainParams.Curve, domainParams.G, domainParams.N, domainParams.H);

        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(ecParams, new SecureRandom()));
        var pair = generator.GenerateKeyPair();

        var privPem = PemEncode("PRIVATE KEY",
            PrivateKeyInfoFactory.CreatePrivateKeyInfo(pair.Private).GetDerEncoded());
        var pubPem = PemEncode("PUBLIC KEY",
            SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(pair.Public).GetDerEncoded());

        return (privPem, pubPem);
    }

    /// <summary>
    /// 手动 PEM 编码（BouncyCastle 的 PemWriter 输出格式略有差异，
    /// 这里保持一致：64 字符换行 + \n 结尾）。
    /// </summary>
    private static string PemEncode(string label, byte[] der)
    {
        var base64 = Convert.ToBase64String(der);
        var sb = new StringBuilder(base64.Length + 64);
        sb.Append("-----BEGIN ").Append(label).Append("-----\n");
        for (var i = 0; i < base64.Length; i += 64)
            sb.Append(base64, i, Math.Min(64, base64.Length - i)).Append('\n');
        sb.Append("-----END ").Append(label).Append("-----\n");
        return sb.ToString();
    }
}
