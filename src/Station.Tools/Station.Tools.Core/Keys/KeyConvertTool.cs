using System.Security.Cryptography;
using System.Text;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Keys;

/// <summary>密钥格式转换工具。</summary>
public static class KeyConvertTool
{
    /// <summary>把 PEM 转换为指定格式。</summary>
    public static ToolResult<string> Convert(string pem, KeyOutputFormat format)
    {
        try
        {
            return format switch
            {
                KeyOutputFormat.Pem => ToolResult<string>.Ok(pem),
                KeyOutputFormat.Base64 => ToolResult<string>.Ok(ToBase64Raw(pem)),
                KeyOutputFormat.Hex => ToolResult<string>.Ok(ToHexRaw(pem)),
                KeyOutputFormat.DerBase64 => ToolResult<string>.Ok(ToDerBase64(pem)),
                _ => ToolResult<string>.Fail("INVALID_FORMAT", "不支持的输出格式")
            };
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("CONVERT_FAIL", ex.Message);
        }
    }

    private static string ToBase64Raw(string pem)
    {
        var content = ExtractPemBody(pem);
        var bytes = System.Convert.FromBase64String(content);
        return System.Convert.ToBase64String(bytes);
    }

    private static string ToHexRaw(string pem)
    {
        var content = ExtractPemBody(pem);
        var bytes = System.Convert.FromBase64String(content);
        return System.Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ToDerBase64(string pem)
    {
        // DER 本身就是二进制，Base64 表达就是去掉 PEM 头尾
        return ExtractPemBody(pem);
    }

    private static string ExtractPemBody(string pem)
    {
        var sb = new StringBuilder();
        foreach (var line in pem.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("-----")) continue;
            if (string.IsNullOrEmpty(trimmed)) continue;
            sb.Append(trimmed);
        }
        return sb.ToString();
    }
}

/// <summary>密钥输出格式。</summary>
public enum KeyOutputFormat
{
    Pem,
    Base64,
    Hex,
    DerBase64
}
