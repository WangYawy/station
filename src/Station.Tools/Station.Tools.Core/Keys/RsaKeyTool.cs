using Station.Crypto.Engine.KeyGen;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Keys;

/// <summary>RSA 密钥工具。</summary>
public static class RsaKeyTool
{
    public static ToolResult<KeyPairResult> Generate(int bits = 2048)
    {
        try
        {
            var (priv, pub) = RsaKeyGenerator.Generate(bits);
            return ToolResult<KeyPairResult>.Ok(new KeyPairResult(priv, pub));
        }
        catch (Exception ex)
        {
            return ToolResult<KeyPairResult>.Fail("KEYGEN_FAIL", ex.Message);
        }
    }
}
