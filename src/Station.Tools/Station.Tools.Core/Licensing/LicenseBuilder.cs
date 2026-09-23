using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Station.Crypto.KeyGen;
using Station.Crypto.Providers.Encryptors;
using Station.Crypto.Providers.Signers;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Licensing;

/// <summary>授权文件构造器。</summary>
public static class LicenseBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions DisplayOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 生成授权文件。
    /// 流程：canonical JSON → SM4 加密 → SM2 签名。
    /// </summary>
    public static ToolResult<string> Generate(
        string stationCode,
        string fingerprint,
        DateTime expiresAt,
        string productCode,
        string privateKeyPem,
        MasterKeyFile masterKeyFile,
        string encryptAlgorithm = "SM4-GCM",
        string signAlgorithm = "SM2-SM3")
    {
        try
        {
            var licenseId = $"LIC-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
            var issuedAt = DateTime.UtcNow;

            var payload = new LicensePayload(
                licenseId, productCode, stationCode, fingerprint, issuedAt, expiresAt);

            // 1. canonical JSON
            var canonical = JsonSerializer.Serialize(payload, JsonOpts);

            // 2. SM4-GCM 加密
            var encryptor = new Sm4GcmEncryptor(
                () => masterKeyFile.Current,
                v => masterKeyFile.GetKey(v));
            var cipher = encryptor.Encrypt(canonical, "license.payload");

            // 3. SM2 签名
            var signer = new Sm2Sm3Signer();
            var signature = signer.Sign(Encoding.UTF8.GetBytes(cipher), privateKeyPem);

            // 4. 组装完整文件
            var file = new LicenseFile(
                licenseId, productCode, stationCode, fingerprint,
                issuedAt, expiresAt, cipher, $"{encryptAlgorithm}+{signAlgorithm}", signature);

            var json = JsonSerializer.Serialize(file, DisplayOpts);
            return ToolResult<string>.Ok(json);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("LICENSE_GEN_FAIL", ex.Message);
        }
    }
}
