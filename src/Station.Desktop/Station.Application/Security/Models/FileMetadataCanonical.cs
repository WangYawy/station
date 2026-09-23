using System.Text.Json;
using System.Text.Json.Serialization;

namespace Station.Application.Security.Models;

/// <summary>
/// 文件元数据规范化模型 + JSON 序列化。
/// 
/// 【字段顺序固定】（C# record 属性声明顺序 = 序列化顺序）
/// 【不含易变字段】如 UploadedAt（避免每次签名不同）
/// </summary>
public sealed record FileMetadataCanonical(
    string FileNo,
    string FileName,
    long Size,
    string ContentDigest,
    string DigestAlgorithm,
    DateTime CollectedAt,
    string StationCode);

/// <summary>规范化 JSON 序列化器。</summary>
public static class FileMetadataCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>生成规范化 JSON 字符串（用于签名的字节来源）。</summary>
    public static string Canonical(FileMetadataCanonical meta) =>
        JsonSerializer.Serialize(meta, Options);

    /// <summary>反序列化。</summary>
    public static FileMetadataCanonical Parse(string json) =>
        JsonSerializer.Deserialize<FileMetadataCanonical>(json, Options)
        ?? throw new InvalidOperationException("元数据 JSON 反序列化失败");
}
