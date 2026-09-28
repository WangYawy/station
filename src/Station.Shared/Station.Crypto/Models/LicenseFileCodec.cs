using System.Text.Json;

namespace Station.Crypto;

/// <summary>
/// 授权文件序列化辅助（Tools 与 App 共用）。
///
/// ⚠️ 序列化选项是持久化契约：
///   - CamelCase 命名
///   - 无缩进 canonical（用于签名）
///   - 缩进展示（用于写盘）
/// 修改选项会破坏已签发授权。
/// </summary>
public static class LicenseFileCodec
{
    private static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private static readonly JsonSerializerOptions DisplayOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>payload → canonical JSON（用于签名对象）。</summary>
    public static string CanonicalPayload(LicensePayload payload) =>
        JsonSerializer.Serialize(payload, CanonicalOpts);

    /// <summary>完整授权文件序列化（写盘用，带缩进）。</summary>
    public static string Serialize(LicenseFile file) =>
        JsonSerializer.Serialize(file, DisplayOpts);

    /// <summary>完整授权文件反序列化。</summary>
    /// <exception cref="InvalidOperationException">JSON 解析失败或为空。</exception>
    public static LicenseFile Parse(string json) =>
        JsonSerializer.Deserialize<LicenseFile>(json, DisplayOpts)
        ?? throw new InvalidOperationException("授权文件解析失败");

    /// <summary>payload 反序列化。</summary>
    /// <exception cref="InvalidOperationException">JSON 解析失败或为空。</exception>
    public static LicensePayload ParsePayload(string json) =>
        JsonSerializer.Deserialize<LicensePayload>(json, CanonicalOpts)
        ?? throw new InvalidOperationException("授权 payload 解析失败");
}
