namespace Station.Contracts.Alerts;

/// <summary>采集站报警上报。</summary>
public sealed record AlertReport
{
    public long StationId { get; init; }

    public long LocalAlertId { get; init; }

    public AlertType Type { get; init; }

    public AlertLevel Level { get; init; }

    /// <summary>来源：端口号 / 设备 ID / 模块名。</summary>
    public required string Source { get; init; }

    public required string Message { get; init; }

    public DateTime OccurredAt { get; init; }
}
