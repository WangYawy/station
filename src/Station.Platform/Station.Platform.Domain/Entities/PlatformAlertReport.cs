using SqlSugar;
using Station.Contracts;

namespace Station.Platform.Domain.Entities;

/// <summary>平台侧报警上报。</summary>
[SugarTable("platform_alert_report")]
[SugarIndex("idx_platform_alert_station", nameof(StationId), OrderByType.Asc)]
public sealed class PlatformAlertReport
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public long StationId { get; set; }

    public long LocalAlertId { get; set; }

    public AlertType Type { get; set; }

    public AlertLevel Level { get; set; }

    [SugarColumn(Length = 64)]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(Length = 512)]
    public string Message { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; }
}
