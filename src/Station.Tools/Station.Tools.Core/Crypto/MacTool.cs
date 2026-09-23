using Station.Crypto.Providers.Macs;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>HMAC 工具。</summary>
public static class MacTool
{
    public static ToolResult<string> Compute(string data, byte[] key, string algorithm)
    {
        try
        {
            var mac = ResolveMac(algorithm);
            var value = mac.Compute(key, System.Text.Encoding.UTF8.GetBytes(data));
            return ToolResult<string>.Ok(value);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("MAC_FAIL", ex.Message);
        }
    }

    public static ToolResult<bool> Verify(string data, byte[] key, string expectedMac, string algorithm)
    {
        try
        {
            var mac = ResolveMac(algorithm);
            return ToolResult<bool>.Ok(mac.Verify(key, System.Text.Encoding.UTF8.GetBytes(data), expectedMac));
        }
        catch (Exception ex)
        {
            return ToolResult<bool>.Fail("MAC_FAIL", ex.Message);
        }
    }

    private static Station.Crypto.Abstractions.IMacProvider ResolveMac(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "HMAC-SM3" => new HmacSm3Provider(),
            "HMAC-SHA256" => new HmacSha256Provider(),
            _ => throw new NotSupportedException($"不支持的 MAC 算法：{algorithm}")
        };
}
