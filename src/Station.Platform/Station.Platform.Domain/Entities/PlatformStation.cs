using SqlSugar;
using Station.Contracts;

namespace Station.Platform.Domain.Entities;

/// <summary>平台侧采集站注册记录。</summary>
[SugarTable("platform_station")]
[SugarIndex("uk_platform_station_code", nameof(StationCode), OrderByType.Asc, true)]
public sealed class PlatformStation
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string StationCode { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string CpuSerial { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string MotherboardSerial { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string DiskSerial { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string MacAddress { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string OsVersion { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string CpuArch { get; set; } = string.Empty;

    [SugarColumn(Length = 32)]
    public string SoftwareVersion { get; set; } = string.Empty;

    public int UsbPortCount { get; set; }

    public LicenseStatus LicenseStatus { get; set; } = LicenseStatus.Trial;

    public bool IsRegistered { get; set; } = true;

    public long ConfigVersion { get; set; }

    public DateTime RegisteredAt { get; set; }
}
