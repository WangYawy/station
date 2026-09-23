using Station.Crypto.Providers.Signers;

namespace Station.Crypto.KeyGen;

/// <summary>SM2 密钥生成器。</summary>
public static class Sm2KeyGenerator
{
    /// <summary>生成 SM2 密钥对，返回 (私钥 PEM, 公钥 PEM)。</summary>
    public static (string PrivatePem, string PublicPem) Generate()
        => Sm2Sm3Signer.GenerateKeyPair();
}
