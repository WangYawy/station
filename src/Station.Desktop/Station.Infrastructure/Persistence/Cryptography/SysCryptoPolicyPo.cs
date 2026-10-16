using SqlSugar;

namespace Station.Infrastructure.Persistence.Cryptography;

[SugarTable("station_crypto_policy")]
[SugarIndex("uk_crypto_usage", nameof(UsageCode), OrderByType.Asc, true)]
public sealed class SysCryptoPolicyPo
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(Length = 32)] public string UsageCode { get; set; } = "";
    [SugarColumn(Length = 64)] public string Algorithm { get; set; } = "";
    [SugarColumn(Length = 64, IsNullable = true)] public string? SecondaryAlgorithm { get; set; }
    [SugarColumn(ColumnDataType = "text", IsNullable = true)] public string? ParamsJson { get; set; }
    [SugarColumn(ColumnDataType = "text", IsNullable = true)] public string? LegacyAlgorithmsJson { get; set; }
    public bool AllowLegacy { get; set; } = true;
    public bool Enabled { get; set; } = true;
    [SugarColumn(Length = 16)] public string Source { get; set; } = "Local";
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? UpdatedBy { get; set; }
}
