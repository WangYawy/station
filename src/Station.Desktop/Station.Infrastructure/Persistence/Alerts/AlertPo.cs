using SqlSugar;
using Station.Contracts;

namespace Station.Infrastructure.Persistence.Alerts;

[SugarTable("station_alert")]
[SugarIndex("idx_alert_created", nameof(CreatedAt), OrderByType.Desc)]
[SugarIndex("idx_alert_status", nameof(Status), OrderByType.Asc)]
public sealed class AlertPo
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public AlertType Type { get; set; }
    public AlertLevel Level { get; set; }
    public AlertStatus Status { get; set; }

    [SugarColumn(Length = 128)]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Detail { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Source { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public long? UpdatedBy { get; set; }
}
