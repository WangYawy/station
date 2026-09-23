using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 加密算法策略表：每个 UsageCode 一行。
/// 修改后由 CryptoPolicyService 通过内存缓存 + 事件通知刷新，无需重启。
/// </summary>
[SugarTable("station_crypto_policy")]
[SugarIndex("uk_crypto_usage", nameof(UsageCode), OrderByType.Asc, true)]
public sealed class SysCryptoPolicy
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>用途：password / license / file_sig / secret_field。</summary>
    [SugarColumn(Length = 32)]
    public string UsageCode { get; set; } = string.Empty;

    /// <summary>主算法标识（见 CryptoAlgorithm 常量）。</summary>
    [SugarColumn(Length = 64)]
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>组合算法第二段（可选）：如授权文件的签名算法。</summary>
    [SugarColumn(Length = 64, IsNullable = true)]
    public string? SecondaryAlgorithm { get; set; }

    /// <summary>算法参数（JSON）。生产环境参数走默认，此处仅保留扩展位。</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? ParamsJson { get; set; }

    /// <summary>
    /// 迁移期允许回退的旧算法列表（JSON 数组，如 ["MD5","PBKDF2-HMAC-SM3"]）。
    /// null 或空数组时使用 CryptoDefaults 提供的默认候选。
    /// </summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? LegacyAlgorithmsJson { get; set; }

    /// <summary>迁移期是否允许旧算法回退校验。迁移完成后应置 false。</summary>
    public bool AllowLegacy { get; set; } = true;

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>策略来源：Local（单机自治）/ Platform（平台下发）。</summary>
    [SugarColumn(Length = 16)]
    public string Source { get; set; } = "Local";

    public DateTime UpdatedAt { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? UpdatedBy { get; set; }
}
