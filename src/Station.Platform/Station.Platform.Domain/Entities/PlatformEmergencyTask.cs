using SqlSugar;

namespace Station.Platform.Domain.Entities;

/// <summary>平台侧紧急优先任务快照（采集站上报，供平台工作台/采集站详情展示）。</summary>
[SugarTable("platform_emergency_task")]
[SugarIndex("idx_platform_emergency_station", nameof(StationId), OrderByType.Asc)]
public sealed class PlatformEmergencyTask
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long StationId { get; set; }

    /// <summary>来源采集站归属部门（数据权限过滤）。</summary>
    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    [SugarColumn(Length = 64)]
    public string TaskNo { get; set; } = string.Empty;

    [SugarColumn(Length = 64)]
    public string RecorderName { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? RecorderSerial { get; set; }

    public int Protocol { get; set; }

    /// <summary>0~1 采集进度。</summary>
    public double Progress { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? StartedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
