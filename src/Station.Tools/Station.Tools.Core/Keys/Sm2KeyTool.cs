using Station.Crypto.Engine.KeyGen;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Keys;

/// <summary>SM2 密钥工具。</summary>
public static class Sm2KeyTool
{
    public static ToolResult<KeyPairResult> Generate()
    {
        try
        {
            var (priv, pub) = Sm2KeyGenerator.Generate();
            return ToolResult<KeyPairResult>.Ok(new KeyPairResult(priv, pub));
        }
        catch (Exception ex)
        {
            return ToolResult<KeyPairResult>.Fail("KEYGEN_FAIL", ex.Message);
        }
    }
}
