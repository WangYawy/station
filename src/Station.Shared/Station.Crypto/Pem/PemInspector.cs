using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;

namespace Station.Crypto.Pem;

/// <summary>PEM 密钥信息检查。</summary>
public static class PemInspector
{
    /// <summary>检查 PEM 内容，返回密钥类型描述。</summary>
    public static PemKeyInfo Inspect(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            throw new ArgumentException("PEM 内容为空");

        var header = ExtractHeader(pem);
        if (header.Contains("PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            // 判断 SM2 还是 RSA
            if (pem.Contains("BEGIN PRIVATE KEY") || pem.Contains("BEGIN EC PRIVATE KEY"))
            {
                try
                {
                    var reader = new PemReader(new StringReader(pem));
                    var obj = reader.ReadObject();
                    if (obj is ECPrivateKeyParameters) return new PemKeyInfo("SM2", "PRIVATE", 256, pem);
                    if (obj is AsymmetricCipherKeyPair kp && kp.Private is ECPrivateKeyParameters)
                        return new PemKeyInfo("SM2", "PRIVATE", 256, pem);
                }
                catch { /* fall through */ }

                try
                {
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(pem);
                    return new PemKeyInfo("RSA", "PRIVATE", rsa.KeySize, pem);
                }
                catch { }
            }
        }
        else if (header.Contains("PUBLIC", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var reader = new PemReader(new StringReader(pem));
                var obj = reader.ReadObject();
                if (obj is ECPublicKeyParameters) return new PemKeyInfo("SM2", "PUBLIC", 256, pem);
                if (obj is AsymmetricCipherKeyPair kp && kp.Public is ECPublicKeyParameters)
                    return new PemKeyInfo("SM2", "PUBLIC", 256, pem);
            }
            catch { }

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                return new PemKeyInfo("RSA", "PUBLIC", rsa.KeySize, pem);
            }
            catch { }
        }

        throw new InvalidOperationException("无法识别的 PEM 格式");
    }

    private static string ExtractHeader(string pem)
    {
        var idx = pem.IndexOf("-----BEGIN", StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        var end = pem.IndexOf("-----", idx + 5, StringComparison.Ordinal);
        return end > idx ? pem[idx..end] : string.Empty;
    }
}

/// <summary>PEM 密钥信息。</summary>
public sealed record PemKeyInfo(string Type, string Kind, int Bits, string RawPem);
