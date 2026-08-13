using Microsoft.Extensions.Configuration;
using Station.Infrastructure.Security;

namespace Station.Platform.Api.Controllers;

/// <summary>平台指令 SM2 密钥解析：显式配置优先，公钥缺失时从私钥推导（同一密钥对）。</summary>
public static class PlatformCommandKeys
{
    public static string? ReadPrivateKey(IConfiguration configuration)
    {
        var privateKey = configuration["Platform:Command:PrivateKeyPem"];
        if (string.IsNullOrWhiteSpace(privateKey) &&
            configuration["Platform:Command:PrivateKeyPemFile"] is { } pemFile &&
            System.IO.File.Exists(pemFile))
        {
            privateKey = System.IO.File.ReadAllText(pemFile).Trim();
        }

        return string.IsNullOrWhiteSpace(privateKey) ? null : privateKey;
    }

    public static string? ReadPublicKey(IConfiguration configuration)
    {
        var publicKey = configuration["Platform:Command:PublicKeyPem"];
        if (string.IsNullOrWhiteSpace(publicKey) &&
            configuration["Platform:Command:PublicKeyPemFile"] is { } pubFile &&
            System.IO.File.Exists(pubFile))
        {
            publicKey = System.IO.File.ReadAllText(pubFile).Trim();
        }

        if (string.IsNullOrWhiteSpace(publicKey) && ReadPrivateKey(configuration) is { } privateKey)
        {
            publicKey = Sm2LicenseSigner.DerivePublicKey(privateKey);
        }

        return string.IsNullOrWhiteSpace(publicKey) ? null : publicKey;
    }
}
