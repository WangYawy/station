using Station.Application.Auditing;
using Station.Domain.Abstractions;
using Station.Domain.Auditing;
using Station.Domain.Collecting;
using Station.Domain.Collecting.Events;

namespace Station.Application.Collecting.EventHandlers;

/// <summary>
/// 任务状态变化 → 审计日志。
/// 状态机每次迁移写一条，含 From/To/原因；紧急标记变化也写。
/// </summary>
public sealed class AuditOnCollectTaskStatusChanged
    : IDomainEventHandler<CollectTaskStatusChanged>
{
    private readonly IAuditLogService _audit;

    public AuditOnCollectTaskStatusChanged(IAuditLogService audit)
        => _audit = audit;

    public async Task HandleAsync(
        CollectTaskStatusChanged e, CancellationToken ct = default)
    {
        var detail = e.FromStatus == e.ToStatus
            ? $"紧急标记变更为 {e.IsEmergency}"
            : $"{e.FromStatus} → {e.ToStatus}"
              + (string.IsNullOrWhiteSpace(e.Reason) ? string.Empty : $"（{e.Reason}）");

        var result = e.ToStatus == CollectTaskStatus.Failed ? 0 : 1;

        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = "system",
            SourceClient = "collect",
            OperationType = "collect-task-status",
            Target = $"CollectTask:{e.AggregateId}",
            Detail = detail,
            Result = result,
            CreatedAt = DateTime.Now
        });
    }
}
