using System.Security.Cryptography;

namespace Station.Crypto.Engine.KeyGen;

/// <summary>RSA 密钥生成器。</summary>
public static class RsaKeyGenerator
{
    /// <summary>
    /// 生成 RSA 密钥对。
    /// </summary>
    /// <param name="bits">密钥位数（2048 / 3072 / 4096）。</param>
    /// <returns>(私钥 PEM, 公钥 PEM) — PKCS#8 / SPKI 格式。</returns>
    /// <exception cref="ArgumentException">位数不在允许范围内。</exception>
    public static (string PrivatePem, string PublicPem) Generate(int bits = 2048)
    {
        if (bits is not (2048 or 3072 or 4096))
            throw new ArgumentException("RSA 位数必须是 2048 / 3072 / 4096", nameof(bits));

        using var rsa = RSA.Create(bits);
        var privPem = rsa.ExportPkcs8PrivateKeyPem();
        var pubPem = rsa.ExportSubjectPublicKeyInfoPem();
        return (privPem, pubPem);
    }
}
