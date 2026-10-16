namespace Station.Domain.Auditing;

/// <summary>
/// 审计日志（操作日志）领域模型。
/// 只写不改的流水记录，无状态机，无领域事件。
/// </summary>
public sealed class AuditLog
{
    public long Id { get; set; }
    public string? OperatorAccount { get; set; }
    public string? OperatorName { get; set; }
    public long? OperatorUserId { get; set; }
    public long? DeptId { get; set; }
    public string? SourceIp { get; set; }

    /// <summary>Desktop / Web / Platform</summary>
    public string? SourceClient { get; set; }

    public string OperationType { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Detail { get; set; }

    /// <summary>1 = 成功，0 = 失败</summary>
    public int Result { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum SourceClient
{
    Desktop,
    Web,
    Platform
}
