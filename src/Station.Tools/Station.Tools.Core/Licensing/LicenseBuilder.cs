using Station.Crypto;
using Station.Crypto.Engine.Internal;
using Station.Crypto.Engine.Licensing;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Licensing;

/// <summary>
/// 授权文件构造器（薄壳，核心逻辑在 <see cref="LicenseEngine"/>）。
/// </summary>
public static class LicenseBuilder
{
    /// <summary>
    /// 生成授权文件。
    /// </summary>
    public static ToolResult<string> Generate(
        string stationCode,
        string fingerprint,
        DateTime expiresAt,
        string productCode,
        string privateKeyPem,
        string signAlgorithm = CryptoAlgorithm.Sm2Sm3)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(privateKeyPem))
                throw new InvalidOperationException("未提供授权签名私钥");

            var licenseId = $"LIC-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
            var issuedAt = DateTime.UtcNow;

            var payload = new LicensePayload(
                licenseId, productCode, stationCode, fingerprint, issuedAt, expiresAt);

            var signer = AlgorithmRegistry.Default.GetSigner(signAlgorithm);
            var json = LicenseEngine.Build(payload, privateKeyPem, signer);

            return ToolResult<string>.Ok(json);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("LICENSE_GEN_FAIL", ex.Message);
        }
    }
}
