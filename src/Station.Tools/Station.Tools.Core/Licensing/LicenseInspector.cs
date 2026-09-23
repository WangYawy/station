using System.Text.Json;
using Station.Crypto.KeyGen;
using Station.Crypto.Providers.Encryptors;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Licensing;

/// <summary>授权文件查看器（不验签，仅展示内容）。</summary>
public static class LicenseInspector
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ToolResult<LicensePayload> Inspect(string licenseJson, MasterKeyFile masterKeyFile)
    {
        try
        {
            var file = JsonSerializer.Deserialize<LicenseFile>(licenseJson, JsonOpts)
                ?? throw new InvalidOperationException("授权文件解析失败");

            if (string.IsNullOrEmpty(file.PayloadCipher))
                throw new InvalidOperationException("这是旧格式授权文件（无加密内容）");

            var encryptor = new Sm4GcmEncryptor(
                () => masterKeyFile.Current,
                v => masterKeyFile.GetKey(v));
            var plain = encryptor.Decrypt(file.PayloadCipher, "license.payload");

            var payload = JsonSerializer.Deserialize<LicensePayload>(plain, JsonOpts)
                ?? throw new InvalidOperationException("授权内容解析失败");

            return ToolResult<LicensePayload>.Ok(payload);
        }
        catch (Exception ex)
        {
            return ToolResult<LicensePayload>.Fail("INSPECT_FAIL", ex.Message);
        }
    }
}
