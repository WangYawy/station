using SqlSugar;
using Station.Contracts;

namespace Station.Domain.Entities;

/// <summary>
/// 报警记录：非授权接入、绑定异常、校验失败、存储不可达等。
/// 界面列表 + 声音/弹窗（P1 邮件短信）。
/// </summary>
[SugarTable("station_alert")]
[SugarIndex("idx_alert_created", nameof(CreatedAt), OrderByType.Desc)]
[SugarIndex("idx_alert_status", nameof(Status), OrderByType.Asc)]
public sealed class Alert
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public AlertType Type { get; set; }

    public AlertLevel Level { get; set; } = AlertLevel.Warning;

    public AlertStatus Status { get; set; } = AlertStatus.Pending;

    [SugarColumn(Length = 128)]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Detail { get; set; }

    /// <summary>来源：记录仪序列号等。</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Source { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? DeptId { get; set; }

    public DateTime CreatedAt { get; set; }
}
