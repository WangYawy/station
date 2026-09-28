using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Station.Crypto.Engine.Keys;

/// <summary>
/// 主密钥文件 JSON DTO（持久化契约）。
///
/// 格式：
/// <code>
/// {
///   "current": 2,
///   "keys": [
///     { "version": 1, "key": "base64(32B)", "createdAt": "2025-01-15T08:00:00Z" },
///     { "version": 2, "key": "base64(32B)", "createdAt": "2025-09-21T10:30:00Z" }
///   ]
/// }
/// </code>
///
/// ⚠️ 该 JSON 结构是持久化契约，字段名变更会破坏已有密钥文件。
/// </summary>
public sealed record MasterKeyFileDto
{
    /// <summary>当前使用的版本号（新数据加密时使用）。</summary>
    [JsonPropertyName("current")]
    public int Current { get; init; } = 1;

    /// <summary>所有密钥版本。</summary>
    [JsonPropertyName("keys")]
    public IReadOnlyList<MasterKeyEntryDto> Keys { get; init; } = Array.Empty<MasterKeyEntryDto>();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>序列化为 JSON。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>
    /// 从 JSON 反序列化。
    /// </summary>
    /// <exception cref="InvalidOperationException">JSON 为空或格式错误。</exception>
    public static MasterKeyFileDto FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<MasterKeyFileDto>(json, JsonOpts)
            ?? throw new InvalidOperationException("主密钥文件格式错误");
    }

    /// <summary>
    /// 生成新的主密钥文件。
    /// </summary>
    /// <param name="keyCount">密钥版本数量（至少 1；默认 1）。</param>
    public static MasterKeyFileDto Create(int keyCount = 1)
    {
        var count = Math.Max(1, keyCount);
        var keys = new List<MasterKeyEntryDto>(count);
        for (var i = 1; i <= count; i++)
        {
            keys.Add(new MasterKeyEntryDto
            {
                Version = i,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                CreatedAt = DateTime.UtcNow
            });
        }
        return new MasterKeyFileDto { Current = count, Keys = keys };
    }

    /// <summary>
    /// 按版本取密钥（32 字节）。
    /// </summary>
    /// <exception cref="MasterKeyNotFoundException">版本不存在。</exception>
    /// <exception cref="CryptoException">密钥长度异常或 Base64 解析失败。</exception>
    public byte[] GetKey(int version)
    {
        foreach (var entry in Keys)
        {
            if (entry.Version != version) continue;

            try
            {
                var key = Convert.FromBase64String(entry.Key);
                if (key.Length != 32)
                    throw new CryptoException(
                        $"主密钥 v{version} 长度异常：{key.Length} 字节（应为 32）");
                return key;
            }
            catch (FormatException ex)
            {
                throw new CryptoException($"主密钥 v{version} Base64 解析失败", ex);
            }
        }

        throw new MasterKeyNotFoundException($"主密钥版本 v{version} 不存在");
    }

    /// <summary>转换为摘要信息（Tools 展示用）。</summary>
    public MasterKeySummary ToSummary()
    {
        var versions = Keys.Select(k => k.Version).OrderBy(v => v).ToArray();
        var createdAt = Keys.Count == 0 ? DateTime.MinValue : Keys.Min(k => k.CreatedAt);
        return new MasterKeySummary(Current, versions, createdAt);
    }
}

/// <summary>单个密钥条目。</summary>
public sealed record MasterKeyEntryDto
{
    /// <summary>版本号（从 1 开始递增）。</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>Base64 编码的密钥（32 字节）。</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>创建时间（UTC）。</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }
}
