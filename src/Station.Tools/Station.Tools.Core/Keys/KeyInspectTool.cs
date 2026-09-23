using System.Text;
using Station.Crypto.Pem;
using Station.Crypto.Providers.Hashers;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Keys;

/// <summary>密钥检查工具。</summary>
public static class KeyInspectTool
{
    /// <summary>检查 PEM 密钥信息。</summary>
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

    /// <summary>计算密钥的 SM3 指纹（Base64）。</summary>
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
        // 去掉头尾空格和换行，得到规范内容
        var canonical = pem.Trim();
        var hasher = new Sm3Hasher();
        return hasher.ComputeHash(Encoding.UTF8.GetBytes(canonical));
    }
}
