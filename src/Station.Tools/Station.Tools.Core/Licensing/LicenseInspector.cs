using Station.Crypto;
using Station.Crypto.Engine.Licensing;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Licensing;

/// <summary>
/// 授权文件查看器（薄壳，核心逻辑在 <see cref="LicenseEngine"/>）。
/// </summary>
public static class LicenseInspector
{
    public static ToolResult<LicensePayload> Inspect(string licenseJson)
    {
        try
        {
            var payload = LicenseEngine.Inspect(licenseJson);
            return payload is null
                ? ToolResult<LicensePayload>.Fail("PARSE_FAIL", "授权文件解析失败")
                : ToolResult<LicensePayload>.Ok(payload);
        }
        catch (Exception ex)
        {
            return ToolResult<LicensePayload>.Fail("INSPECT_FAIL", ex.Message);
        }
    }
}
