using Station.Crypto;
using Station.Crypto.Engine.Internal;
using Station.Crypto.Engine.Licensing;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Licensing;

/// <summary>
/// 授权文件验证器（薄壳，核心逻辑在 <see cref="LicenseEngine"/>）。
/// </summary>
public static class LicenseValidator
{
    public static ToolResult<LicenseValidateResult> Validate(
        string licenseJson,
        string publicKeyPem,
        string? expectedStationCode = null,
        string? expectedFingerprint = null)
    {
        try
        {
            var result = LicenseEngine.Validate(
                licenseJson,
                publicKeyPem,
                AlgorithmRegistry.Default.GetSigner,
                expectedStationCode,
                expectedFingerprint);

            return ToolResult<LicenseValidateResult>.Ok(result);
        }
        catch (Exception ex)
        {
            return ToolResult<LicenseValidateResult>.Fail("VALIDATE_FAIL", ex.Message);
        }
    }
}
