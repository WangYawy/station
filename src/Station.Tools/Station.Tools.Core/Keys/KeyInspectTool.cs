using System.Text;
using Station.Crypto.Engine.Hashers;
using Station.Crypto.Engine.Pem;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Keys;

/// <summary>密钥检查工具。</summary>
public static class KeyInspectTool
{
    public static ToolResult<KeyInfo> Inspect(string pem)
    {
        try
        {
            var info = PemInspector.Inspect(pem);
            var fp = ComputeFingerprint(pem);
            return ToolResult<KeyInfo>.Ok(new KeyInfo(info.Type, info.Kind, info.Bits, fp));
        }
        catch (Exception ex)
        {
            return ToolResult<KeyInfo>.Fail("INSPECT_FAIL", ex.Message);
        }
    }

    public static ToolResult<string> Fingerprint(string pem)
    {
        try
        {
            return ToolResult<string>.Ok(ComputeFingerprint(pem));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("FINGERPRINT_FAIL", ex.Message);
        }
    }

    private static string ComputeFingerprint(string pem)
    {
        var canonical = pem.Trim();
        return new Sm3Hasher().ComputeHash(Encoding.UTF8.GetBytes(canonical));
    }
}
