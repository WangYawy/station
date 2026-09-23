using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Station.Crypto.KeyGen;

/// <summary>主密钥文件（JSON 格式）。</summary>
public sealed class MasterKeyFile
{
    [JsonPropertyName("current")]
    public int Current { get; set; } = 1;

    [JsonPropertyName("keys")]
    public List<MasterKeyEntry> Keys { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static MasterKeyFile FromJson(string json) =>
        JsonSerializer.Deserialize<MasterKeyFile>(json, JsonOpts)
        ?? throw new InvalidOperationException("主密钥文件格式错误");

    /// <summary>生成新的主密钥文件（默认 1 个版本）。</summary>
    public static MasterKeyFile Create(int keyCount = 1)
    {
        var file = new MasterKeyFile();
        for (var i = 1; i <= Math.Max(1, keyCount); i++)
        {
            file.Keys.Add(new MasterKeyEntry
            {
                Version = i,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                CreatedAt = DateTime.UtcNow
            });
        }
        file.Current = file.Keys.Max(k => k.Version);
        return file;
    }

    /// <summary>按版本取密钥。</summary>
    public byte[] GetKey(int version)
    {
        var entry = Keys.FirstOrDefault(k => k.Version == version)
            ?? throw new InvalidOperationException($"主密钥版本 v{version} 不存在");
        return Convert.FromBase64String(entry.Key);
    }
}

/// <summary>单个密钥条目。</summary>
public sealed class MasterKeyEntry
{
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
}
