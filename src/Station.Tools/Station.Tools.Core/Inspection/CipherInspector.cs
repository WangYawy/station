using Station.Tools.Core.Models;

namespace Station.Tools.Core.Inspection;

/// <summary>密文格式判断。</summary>
public static class CipherInspector
{
    public static ToolResult<CipherInfo> Inspect(string text)
    {
        if (string.IsNullOrEmpty(text))
            return ToolResult<CipherInfo>.Fail("EMPTY", "输入为空");

        // 检测 v{n}:base64
        if (text[0] == 'v' && text.Length > 3)
        {
            var idx = text.IndexOf(':');
            if (idx >= 2 && int.TryParse(text.AsSpan(1, idx - 1), out var version))
            {
                return ToolResult<CipherInfo>.Ok(new CipherInfo("VERSIONED_CIPHER", version, null));
            }
        }

        // 检测 sm3$ / md5$ / pbkdf2-sha256$
        if (text.StartsWith("sm3$")) return ToolResult<CipherInfo>.Ok(new CipherInfo("HASH", null, "PBKDF2-HMAC-SM3"));
        if (text.StartsWith("pbkdf2-sha256$")) return ToolResult<CipherInfo>.Ok(new CipherInfo("HASH", null, "PBKDF2-SHA256"));
        if (text.StartsWith("md5$")) return ToolResult<CipherInfo>.Ok(new CipherInfo("HASH", null, "MD5"));
        if (text.Length == 32 && text.All(Uri.IsHexDigit)) return ToolResult<CipherInfo>.Ok(new CipherInfo("HASH", null, "MD5-raw"));

        // Base64（可能是密文或摘要）
        if (IsBase64(text)) return ToolResult<CipherInfo>.Ok(new CipherInfo("BASE64", null, null));

        // 明文
        return ToolResult<CipherInfo>.Ok(new CipherInfo("PLAINTEXT", null, null));
    }

    private static bool IsBase64(string s)
    {
        try { Convert.FromBase64String(s); return true; }
        catch { return false; }
    }
}

/// <summary>密文信息。</summary>
public sealed record CipherInfo(string Kind, int? Version, string? Algorithm);
