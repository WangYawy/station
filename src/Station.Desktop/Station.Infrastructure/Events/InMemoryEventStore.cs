using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Station.Domain.Abstractions;

namespace Station.Infrastructure.Events;

/// <summary>
/// 阶段一：内存事件保留，用于有限窗口的事件重放。
/// 进程重启后丢失。阶段二由 Outbox 表替换。
/// </summary>
public sealed class InMemoryEventStore
{
    private readonly ConcurrentQueue<IDomainEvent> _events = new();
    private readonly DomainEventBusOptions _options;

    public InMemoryEventStore(IOptions<DomainEventBusOptions> options)
        => _options = options.Value;

    public void Append(IDomainEvent domainEvent)
    {
        _events.Enqueue(domainEvent);
        Trim();
    }

    public void AppendRange(IEnumerable<IDomainEvent> events)
    {
        foreach (var e in events) _events.Enqueue(e);
        Trim();
    }

    public IReadOnlyList<IDomainEvent> Query(DateTime from, DateTime to, string? aggregateType = null)
    {
        var result = new List<IDomainEvent>();
        foreach (var e in _events)
        {
            if (e.OccurredAt < from || e.OccurredAt > to) continue;
            if (aggregateType is not null && e.AggregateType != aggregateType) continue;
            result.Add(e);
        }
        return result;
    }

    private void Trim()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);

        while (_events.TryPeek(out var oldest) && oldest.OccurredAt < cutoff)
            _events.TryDequeue(out _);

        while (_events.Count > _options.RetentionMaxCount && _events.TryDequeue(out _))
        {
            // 超量丢弃
        }
    }
}
