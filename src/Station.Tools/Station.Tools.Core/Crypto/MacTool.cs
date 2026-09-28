using System.Text;
using Station.Crypto.Engine.Internal;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>HMAC 工具。</summary>
public static class MacTool
{
    public static ToolResult<string> Compute(string data, byte[] key, string algorithm)
    {
        try
        {
            var mac = AlgorithmRegistry.Default.GetMacProvider(algorithm);
            return ToolResult<string>.Ok(mac.Compute(key, Encoding.UTF8.GetBytes(data)));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("MAC_FAIL", ex.Message);
        }
    }

    public static ToolResult<bool> Verify(
        string data, byte[] key, string expectedMac, string algorithm)
    {
        try
        {
            var mac = AlgorithmRegistry.Default.GetMacProvider(algorithm);
            return ToolResult<bool>.Ok(
                mac.Verify(key, Encoding.UTF8.GetBytes(data), expectedMac));
        }
        catch (Exception ex)
        {
            return ToolResult<bool>.Fail("MAC_FAIL", ex.Message);
        }
    }
}
