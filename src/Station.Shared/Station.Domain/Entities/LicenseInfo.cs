using SqlSugar;
using Station.Contracts;

namespace Station.Domain.Entities;

/// <summary>本地授权记录（离线激活，绑定采集站硬件指纹）。</summary>
[SugarTable("station_license")]
[SugarIndex("uk_license_key", nameof(LicenseKey), OrderByType.Asc, true)]
public sealed class LicenseInfo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string LicenseKey { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string ProductCode { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string StationCode { get; set; } = string.Empty;

    /// <summary>绑定硬件指纹（SM3）。</summary>
    [SugarColumn(Length = 64)]
    public string Fingerprint { get; set; } = string.Empty;

    public DateTime IssuedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public LicenseStatus Status { get; set; } = LicenseStatus.Activated;

    public DateTime? ActivatedAt { get; set; }

    public bool IsActive { get; set; } = true;
}
