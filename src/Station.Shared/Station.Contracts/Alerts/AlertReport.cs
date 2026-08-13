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

    /// <summary>站侧 SM2 签名（平台验签，防伪造上报）。</summary>
    public string? Signature { get; init; }
}

/// <summary>报警上报签名规范化。</summary>
public static class AlertReportSignature
{
    public static string Canonical(AlertReport report) =>
        $"{report.StationId}|{report.LocalAlertId}|{(int)report.Type}|{(int)report.Level}|{report.Source}|{report.Message}|{report.OccurredAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ss}";
}
