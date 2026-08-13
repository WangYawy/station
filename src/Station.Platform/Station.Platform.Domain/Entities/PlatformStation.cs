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

    /// <summary>运行状态：正常/维修/报废。</summary>
    public StationOperationalStatus OperationalStatus { get; set; } = StationOperationalStatus.Normal;

    public bool IsRegistered { get; set; } = true;

    public long ConfigVersion { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LicenseExpiresAt { get; set; }

    public int LicenseDaysLeft { get; set; }

    /// <summary>最近一次心跳（任意站→平台入站请求），用于在线率统计。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? LastHeartbeatAt { get; set; }

    /// <summary>采集站归属部门（平台组织树节点），数据权限按部门过滤。</summary>
    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? StationBaseUrl { get; set; }

    public DateTime RegisteredAt { get; set; }
}
