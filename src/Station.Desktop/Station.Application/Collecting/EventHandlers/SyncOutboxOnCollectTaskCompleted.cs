using System.Text.Json;
using Station.Data.IdGeneration;
using Station.Domain.Abstractions;
using Station.Domain.Collecting.Events;
using Station.Domain.Outbox;

namespace Station.Application.Collecting.EventHandlers;

/// <summary>
/// 任务完成 → 写 SyncOutbox，触发平台同步。
/// 
/// 注意：SyncOutbox 字段名以你项目实际定义为准，如字段不同请对齐。
/// </summary>
public sealed class SyncOutboxOnCollectTaskCompleted : IDomainEventHandler<CollectTaskCompleted>
{
    private readonly ISyncOutboxRepository _outbox;
    private readonly IIdGenerator _idGenerator;

    public SyncOutboxOnCollectTaskCompleted(
        ISyncOutboxRepository outbox,
        IIdGenerator idGenerator)
    {
        _outbox = outbox;
        _idGenerator = idGenerator;
    }

    public async Task HandleAsync(
        CollectTaskCompleted e, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            taskId = e.AggregateId,
            totalFiles = e.TotalFiles,
            collectedFiles = e.CollectedFiles,
            failedFiles = e.FailedFiles,
            collectedBytes = e.CollectedBytes,
            completedAt = e.CompletedAt
        });

        var entity = SyncOutboxItem.Create(_idGenerator.NewId(), "", payload, DateTime.Now);
        await _outbox.AddAsync(entity, ct);
    }
}
