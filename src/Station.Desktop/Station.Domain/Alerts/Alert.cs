using Station.Contracts;

namespace Station.Domain.Alerts;

/// <summary>
/// 报警聚合根。
/// 状态机：Pending → Processing → Handled / Dismissed
/// </summary>
public sealed class Alert
{
    public long Id { get; private set; }
    public AlertType Type { get; private set; }
    public AlertLevel Level { get; private set; }
    public AlertStatus Status { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string? Source { get; private set; }
    public long? DeptId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public long? UpdatedBy { get; private set; }

    private Alert() { }

    // =========================================================
    // 工厂
    // =========================================================

    public static Alert Create(
        long id,
        AlertType type,
        AlertLevel level,
        string title,
        string? detail,
        string? source,
        long? deptId,
        DateTime now)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("报警标题不能为空。", nameof(title));

        return new Alert
        {
            Id = id,
            Type = type,
            Level = level,
            Status = AlertStatus.Pending,
            Title = title,
            Detail = detail,
            Source = source,
            DeptId = deptId,
            CreatedAt = now
        };
    }

    public static Alert Restore(
        long id, AlertType type, AlertLevel level, AlertStatus status,
        string title, string? detail, string? source, long? deptId,
        DateTime createdAt, DateTime? updatedAt, long? updatedBy)
        => new()
        {
            Id = id,
            Type = type,
            Level = level,
            Status = status,
            Title = title,
            Detail = detail,
            Source = source,
            DeptId = deptId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy
        };

    // =========================================================
    // 行为
    // =========================================================

    public void StartProcessing(DateTime now, long? updatedBy = null)
    {
        if (Status != AlertStatus.Pending)
            throw new InvalidOperationException($"状态 {Status} 不能进入处理中。");
        Status = AlertStatus.Processed;
        Touch(now, updatedBy);
    }

    public void MarkHandled(DateTime now, long? updatedBy = null)
    {
        if (Status == AlertStatus.Confirmed) return;
        Status = AlertStatus.Confirmed;
        Touch(now, updatedBy);
    }

    public void Dismiss(DateTime now, long? updatedBy = null)
    {
        if (Status == AlertStatus.Closed) return;
        Status = AlertStatus.Closed;
        Touch(now, updatedBy);
    }

    private void Touch(DateTime now, long? updatedBy)
    {
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
