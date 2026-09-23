using Station.Crypto.Providers.Hashers;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>摘要工具。</summary>
public static class HashTool
{
    /// <summary>计算字符串摘要。</summary>
    public static ToolResult<string> Hash(string text, string algorithm)
    {
        try
        {
            var hasher = ResolveHasher(algorithm);
            var digest = hasher.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
            return ToolResult<string>.Ok(digest);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("HASH_FAIL", ex.Message);
        }
    }

    /// <summary>计算文件摘要。</summary>
    public static async Task<ToolResult<string>> HashFileAsync(
        string filePath, string algorithm, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return ToolResult<string>.Fail("FILE_NOT_FOUND", $"文件不存在：{filePath}");

            var hasher = ResolveHasher(algorithm);
            await using var fs = File.OpenRead(filePath);
            var digest = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);
            return ToolResult<string>.Ok(digest);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("HASH_FAIL", ex.Message);
        }
    }

    internal static Station.Crypto.Abstractions.IHasher ResolveHasher(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "SM3" => new Sm3Hasher(),
            "SHA-256" or "SHA256" => new Sha256Hasher(),
            _ => throw new NotSupportedException($"不支持的摘要算法：{algorithm}")
        };
}
