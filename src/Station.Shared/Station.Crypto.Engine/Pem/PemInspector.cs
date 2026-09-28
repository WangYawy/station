using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;

namespace Station.Crypto.Engine.Pem;

/// <summary>PEM 密钥信息检查。</summary>
public static class PemInspector
{
    /// <summary>
    /// 检查 PEM 内容，返回密钥类型描述。
    /// </summary>
    /// <exception cref="ArgumentException">PEM 内容为空。</exception>
    /// <exception cref="InvalidOperationException">无法识别的 PEM 格式。</exception>
    public static PemKeyInfo Inspect(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            throw new ArgumentException("PEM 内容为空", nameof(pem));

        var header = ExtractHeader(pem);
        if (header.Contains("PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            var priv = TryInspectPrivateKey(pem);
            if (priv is not null) return priv;
        }
        else if (header.Contains("PUBLIC", StringComparison.OrdinalIgnoreCase))
        {
            var pub = TryInspectPublicKey(pem);
            if (pub is not null) return pub;
        }

        throw new InvalidOperationException("无法识别的 PEM 格式");
    }

    private static PemKeyInfo? TryInspectPrivateKey(string pem)
    {
        // 先试 SM2（BouncyCastle）
        if (pem.Contains("BEGIN PRIVATE KEY") || pem.Contains("BEGIN EC PRIVATE KEY"))
        {
            try
            {
                var reader = new PemReader(new StringReader(pem));
                var obj = reader.ReadObject();
                switch (obj)
                {
                    case ECPrivateKeyParameters:
                        return new PemKeyInfo("SM2", "PRIVATE", 256, pem);
                    case AsymmetricCipherKeyPair kp when kp.Private is ECPrivateKeyParameters:
                        return new PemKeyInfo("SM2", "PRIVATE", 256, pem);
                }
            }
            catch { /* fall through */ }

            // 再试 RSA（.NET BCL）
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                return new PemKeyInfo("RSA", "PRIVATE", rsa.KeySize, pem);
            }
            catch { /* fall through */ }
        }

        return null;
    }

    private static PemKeyInfo? TryInspectPublicKey(string pem)
    {
        // 先试 SM2
        try
        {
            var reader = new PemReader(new StringReader(pem));
            var obj = reader.ReadObject();
            switch (obj)
            {
                case ECPublicKeyParameters:
                    return new PemKeyInfo("SM2", "PUBLIC", 256, pem);
                case AsymmetricCipherKeyPair kp when kp.Public is ECPublicKeyParameters:
                    return new PemKeyInfo("SM2", "PUBLIC", 256, pem);
            }
        }
        catch { /* fall through */ }

        // 再试 RSA
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return new PemKeyInfo("RSA", "PUBLIC", rsa.KeySize, pem);
        }
        catch { /* fall through */ }

        return null;
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
/// <param name="Type">"SM2" / "RSA"。</param>
/// <param name="Kind">"PRIVATE" / "PUBLIC"。</param>
/// <param name="Bits">密钥位数（SM2 固定 256）。</param>
/// <param name="RawPem">原始 PEM 内容。</param>
public sealed record PemKeyInfo(string Type, string Kind, int Bits, string RawPem);
