using Station.Application.Auditing;
using Station.Domain.Abstractions;
using Station.Domain.Auditing;
using Station.Domain.Collecting.Events;

namespace Station.Application.Collecting.EventHandlers;

/// <summary>文件上传成功 → 审计日志。</summary>
public sealed class AuditOnCollectFileUploaded
    : IDomainEventHandler<CollectFileUploaded>
{
    private readonly IAuditLogService _audit;

    public AuditOnCollectFileUploaded(IAuditLogService audit)
        => _audit = audit;

    public async Task HandleAsync(
        CollectFileUploaded e, CancellationToken ct = default)
    {
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = "system",
            SourceClient = "upload",
            OperationType = "collect-file-uploaded",
            Target = $"CollectFile:{e.AggregateId}",
            Detail = $"文件 {e.FileName}（{e.Size} 字节）上传成功，远端 {e.RemotePath}",
            Result = 1,
            CreatedAt = DateTime.Now
        });
    }
}
