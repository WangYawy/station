using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

public sealed record CollectTaskCompleted(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    int TotalFiles,
    int CollectedFiles,
    int FailedFiles,
    long CollectedBytes,
    DateTime CompletedAt) : IDomainEvent;
