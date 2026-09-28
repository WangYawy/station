using System.Text;
using Station.Crypto.Engine.Internal;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>摘要工具。</summary>
public static class HashTool
{
    public static ToolResult<string> Hash(string text, string algorithm)
    {
        try
        {
            var hasher = AlgorithmRegistry.Default.GetHasher(algorithm);
            var digest = hasher.ComputeHash(Encoding.UTF8.GetBytes(text));
            return ToolResult<string>.Ok(digest);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("HASH_FAIL", ex.Message);
        }
    }

    public static async Task<ToolResult<string>> HashFileAsync(
        string filePath, string algorithm, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return ToolResult<string>.Fail("FILE_NOT_FOUND", $"文件不存在：{filePath}");

            var hasher = AlgorithmRegistry.Default.GetHasher(algorithm);
            await using var fs = File.OpenRead(filePath);
            var digest = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);
            return ToolResult<string>.Ok(digest);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("HASH_FAIL", ex.Message);
        }
    }
}
