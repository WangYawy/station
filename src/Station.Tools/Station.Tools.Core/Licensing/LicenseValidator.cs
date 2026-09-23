using System.Text;
using System.Text.Json;
using Station.Crypto.KeyGen;
using Station.Crypto.Providers.Encryptors;
using Station.Crypto.Providers.Signers;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Licensing;

/// <summary>授权文件验证器。</summary>
public static class LicenseValidator
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>验证授权文件（验签 + 解密）。</summary>
    public static ToolResult<LicenseCheckResult> Validate(
        string licenseJson,
        string publicKeyPem,
        MasterKeyFile masterKeyFile,
        string? expectedStationCode = null,
        string? expectedFingerprint = null)
    {
        try
        {
            var file = JsonSerializer.Deserialize<LicenseFile>(licenseJson, JsonOpts)
                ?? throw new InvalidOperationException("授权文件解析失败");

            // 1. 验签
            var signer = new Sm2Sm3Signer();
            if (!signer.Verify(Encoding.UTF8.GetBytes(file.PayloadCipher), file.Signature, publicKeyPem))
                return ToolResult<LicenseCheckResult>.Ok(new LicenseCheckResult(false, "签名无效", null));

            // 2. 解密
            var encryptor = new Sm4GcmEncryptor(
                () => masterKeyFile.Current,
                v => masterKeyFile.GetKey(v));
            var plain = encryptor.Decrypt(file.PayloadCipher, "license.payload");
            var payload = JsonSerializer.Deserialize<LicensePayload>(plain, JsonOpts)
                ?? throw new InvalidOperationException("授权内容解析失败");

            // 3. 业务校验
            if (!string.IsNullOrEmpty(expectedStationCode)
                && !string.Equals(payload.StationCode, expectedStationCode, StringComparison.OrdinalIgnoreCase))
                return ToolResult<LicenseCheckResult>.Ok(new LicenseCheckResult(false, "站点编号不匹配", payload));

            if (!string.IsNullOrEmpty(expectedFingerprint)
                && !string.Equals(payload.Fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
                return ToolResult<LicenseCheckResult>.Ok(new LicenseCheckResult(false, "硬件指纹不匹配", payload));

            if (payload.ExpiresAt < DateTime.UtcNow)
                return ToolResult<LicenseCheckResult>.Ok(new LicenseCheckResult(false, "授权已过期", payload));

            return ToolResult<LicenseCheckResult>.Ok(new LicenseCheckResult(true, "授权有效", payload));
        }
        catch (Exception ex)
        {
            return ToolResult<LicenseCheckResult>.Fail("VALIDATE_FAIL", ex.Message);
        }
    }
}
