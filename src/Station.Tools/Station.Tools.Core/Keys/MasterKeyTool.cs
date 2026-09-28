using Station.Crypto;
using Station.Crypto.Engine.Keys;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Keys;

/// <summary>主密钥文件工具。</summary>
public static class MasterKeyTool
{
    public static ToolResult<string> Generate(int keyCount = 1)
    {
        try
        {
            if (keyCount < 1 || keyCount > 100)
                return ToolResult<string>.Fail("INVALID_ARG", "密钥数量应在 1-100 之间");

            var dto = MasterKeyFileDto.Create(keyCount);
            return ToolResult<string>.Ok(dto.ToJson());
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("KEYGEN_FAIL", ex.Message);
        }
    }

    public static ToolResult<MasterKeySummary> Inspect(string json)
    {
        try
        {
            var dto = MasterKeyFileDto.FromJson(json);
            return ToolResult<MasterKeySummary>.Ok(dto.ToSummary());
        }
        catch (Exception ex)
        {
            return ToolResult<MasterKeySummary>.Fail("PARSE_FAIL", ex.Message);
        }
    }
}
