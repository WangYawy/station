using Station.Crypto.KeyGen;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Keys;

/// <summary>主密钥文件工具。</summary>
public static class MasterKeyTool
{
    /// <summary>生成主密钥文件 JSON。</summary>
    public static ToolResult<string> Generate(int keyCount = 1)
    {
        try
        {
            if (keyCount < 1 || keyCount > 100)
                return ToolResult<string>.Fail("INVALID_ARG", "密钥数量应在 1-100 之间");

            var file = MasterKeyFile.Create(keyCount);
            return ToolResult<string>.Ok(file.ToJson());
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("KEYGEN_FAIL", ex.Message);
        }
    }

    /// <summary>检查主密钥文件。</summary>
    public static ToolResult<MasterKeySummary> Inspect(string json)
    {
        try
        {
            var file = MasterKeyFile.FromJson(json);
            return ToolResult<MasterKeySummary>.Ok(new MasterKeySummary(
                Current: file.Current,
                Versions: file.Keys.Select(k => k.Version).ToArray(),
                CreatedAt: file.Keys.Min(k => k.CreatedAt)));
        }
        catch (Exception ex)
        {
            return ToolResult<MasterKeySummary>.Fail("PARSE_FAIL", ex.Message);
        }
    }
}

/// <summary>主密钥摘要信息。</summary>
public sealed record MasterKeySummary(int Current, int[] Versions, DateTime CreatedAt);
