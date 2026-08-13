using SqlSugar;
using Station.Contracts;

namespace Station.Platform.Domain.Entities;

/// <summary>平台侧记录仪台账（全站汇总）：随文件元数据上报自动归集，含绑定信息与白名单。</summary>
[SugarTable("platform_recorder")]
[SugarIndex("uk_platform_recorder_serial", nameof(RecorderSerial), OrderByType.Asc, true)]
public sealed class PlatformRecorder
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    [SugarColumn(Length = 64)]
    public string RecorderSerial { get; set; } = string.Empty;

    /// <summary>最近一次上报该记录仪的采集站。</summary>
    [SugarColumn(IsNullable = true)]
    public long? LastStationId { get; set; }

    /// <summary>最近一次上报采集站的归属部门（数据权限过滤）。</summary>
    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    [SugarColumn(IsNullable = true)]
    public ProtocolType? Protocol { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public long FileCount { get; set; }

    public long TotalSize { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastFileAt { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? BoundUserNo { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? BoundUserName { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? BoundDeptCode { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? BoundDeptName { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? BoundAt { get; set; }

    /// <summary>白名单：是否允许接入（与采集站本地台账 IsAuthorized 对应）。</summary>
    public bool IsWhitelisted { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; }
}
