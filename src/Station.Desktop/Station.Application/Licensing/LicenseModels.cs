using System.Text.Json;
using System.Text.Json.Serialization;
using Station.Domain.Security;

namespace Station.Application.Licensing;

/// <summary>授权文件（station.lic）：内部工具生成，采集站校验。</summary>
public sealed record LicenseFile(
    string LicenseKey,
    string ProductCode,
    string StationCode,
    string Fingerprint,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    string PayloadCipher,
    string Algo,
    string Signature);

/// <summary>
/// 授权文件序列化/规范化。
/// CanonicalJson 用于签名前的规范化（属性顺序固定、无空格）。
/// </summary>
public static class LicenseFileCodec
{
    /// <summary>规范化选项：紧凑输出、空值忽略、camelCase。</summary>
    private static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,   // 保证空串也输出，字段顺序稳定
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>展示用选项：缩进输出，便于人工阅读。</summary>
    private static readonly JsonSerializerOptions DisplayOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 生成规范化 JSON（用于签名）。
    /// 必须剔除 Signature 与 PayloadCipher 字段本身，避免循环依赖。
    /// </summary>
    public static string Canonical(LicenseFile file) =>
        JsonSerializer.Serialize(
            file with { Signature = string.Empty, PayloadCipher = string.Empty },
            CanonicalOpts);

    /// <summary>序列化完整授权文件（含密文与签名，缩进便于人工核对）。</summary>
    public static string Serialize(LicenseFile file) =>
        JsonSerializer.Serialize(file, DisplayOpts);


    /// <summary>反序列化（严格，字段缺失抛异常）。</summary>
    public static LicenseFile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("授权文件为空");

        try
        {
            return JsonSerializer.Deserialize<LicenseFile>(json, DisplayOpts)
                   ?? throw new InvalidOperationException("反序列化返回 null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"JSON 解析失败：{ex.Message}", ex);
        }
    }

}

/// <summary>授权检查结果。</summary>
public sealed record LicenseCheckResult(
    Station.Contracts.LicenseStatus Status,
    DateTime? ExpiresAt,
    int DaysLeft,
    bool IsValid,
    string Message);
