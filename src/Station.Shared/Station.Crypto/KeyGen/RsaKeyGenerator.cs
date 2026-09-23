using Station.Crypto.Providers.Signers;

namespace Station.Crypto.KeyGen;

/// <summary>RSA 密钥生成器。</summary>
public static class RsaKeyGenerator
{
    /// <summary>生成 RSA 密钥对，返回 (私钥 PEM, 公钥 PEM)。</summary>
    public static (string PrivatePem, string PublicPem) Generate(int bits = 2048)
    {
        if (bits is not (2048 or 3072 or 4096))
            throw new ArgumentException("RSA 位数必须是 2048 / 3072 / 4096", nameof(bits));
        return RsaSha256Signer.GenerateKeyPair(bits);
    }
}
