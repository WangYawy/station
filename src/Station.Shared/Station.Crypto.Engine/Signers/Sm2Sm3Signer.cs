using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace Station.Crypto.Engine.Signers;

/// <summary>
/// SM2 签名 + SM3 摘要。
/// 密钥格式：PEM（PKCS#8 / SEC1 / SPKI）。
///
/// ⚠️ Sign 传入的应是原始数据；SM2 内部已含 SM3 摘要，不要预哈希。
/// </summary>
public sealed class Sm2Sm3Signer : ISigner
{
    /// <summary>默认用户 ID（国密规范推荐值）。</summary>
    private static readonly byte[] DefaultUserId = Encoding.UTF8.GetBytes("1234567812345678");

    public string Algorithm => CryptoAlgorithm.Sm2Sm3;

    public string Sign(byte[] data, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        var priv = LoadPrivateKey(privateKeyPem);
        var signer = new SM2Signer();
        signer.Init(true, new ParametersWithID(
            new ParametersWithRandom(priv, new SecureRandom()), DefaultUserId));
        signer.BlockUpdate(data, 0, data.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public bool Verify(byte[] data, string signatureBase64, string publicKeyPem)
    {
        if (data is null
            || string.IsNullOrWhiteSpace(signatureBase64)
            || string.IsNullOrWhiteSpace(publicKeyPem))
            return false;

        try
        {
            var pub = LoadPublicKey(publicKeyPem);
            var signature = Convert.FromBase64String(signatureBase64);
            var signer = new SM2Signer();
            signer.Init(false, new ParametersWithID(pub, DefaultUserId));
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature);
        }
        catch { return false; }
    }

    private static ECPrivateKeyParameters LoadPrivateKey(string pem)
    {
        var reader = new PemReader(new StringReader(pem));
        var obj = reader.ReadObject();
        return obj switch
        {
            ECPrivateKeyParameters p => p,
            AsymmetricCipherKeyPair kp => (ECPrivateKeyParameters)kp.Private,
            _ => throw new CryptoException("无法解析 SM2 私钥 PEM")
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
            _ => throw new CryptoException("无法解析 SM2 公钥 PEM")
        };
    }
}
