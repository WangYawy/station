namespace Station.Contracts.Reporting;

/// <summary>站→平台紧急优先任务快照上报（平台版工作台/采集站详情展示紧急优先）。</summary>
public sealed record EmergencyTaskItem(
    string TaskNo,
    string RecorderName,
    string? RecorderSerial,
    int Protocol,
    double Progress,
    DateTime? StartedAt);

public sealed record EmergencyTaskReport(
    long StationId,
    IReadOnlyList<EmergencyTaskItem> Tasks);
