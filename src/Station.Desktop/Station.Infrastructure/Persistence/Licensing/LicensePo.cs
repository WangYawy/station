using SqlSugar;
using Station.Contracts;

namespace Station.Infrastructure.Persistence.Licensing;

[SugarTable("station_license")]
[SugarIndex("uk_license_key", nameof(LicenseKey), OrderByType.Asc, true)]
public sealed class LicensePo
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(Length = 64)] public string LicenseKey { get; set; } = "";
    [SugarColumn(Length = 32)] public string ProductCode { get; set; } = "";
    [SugarColumn(Length = 64)] public string StationCode { get; set; } = "";
    [SugarColumn(Length = 64)] public string Fingerprint { get; set; } = "";
    [SugarColumn(Length = 8192, IsNullable = true)] public string? PayloadEnc { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public LicenseStatus Status { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? ActivatedAt { get; set; }
    public bool IsActive { get; set; }
}
