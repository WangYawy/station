using System.Text.Json;

namespace Station.Crypto;

/// <summary>
/// 文件元数据（用于签名）。
/// 签名对象是 <see cref="FileMetadataCodec.Canonical"/> 生成的 canonical JSON。
/// </summary>
public sealed record FileMetadata(
    string FileNo,
    string FileName,
    long Size,
    string ContentDigest,
    string DigestAlgorithm,
    DateTime CollectedAt,
    string StationCode);

/// <summary>元数据规范化 JSON 序列化器。</summary>
public static class FileMetadataCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>生成规范化 JSON（用于签名）。</summary>
    public static string Canonical(FileMetadata meta) =>
        JsonSerializer.Serialize(meta, Options);

    /// <summary>反序列化。</summary>
    /// <exception cref="InvalidOperationException">JSON 解析失败或为空。</exception>
    public static FileMetadata Parse(string json) =>
        JsonSerializer.Deserialize<FileMetadata>(json, Options)
        ?? throw new InvalidOperationException("元数据 JSON 反序列化失败");
}
