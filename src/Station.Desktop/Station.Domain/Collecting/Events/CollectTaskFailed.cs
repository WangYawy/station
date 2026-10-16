using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

public sealed record CollectTaskFailed(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    string ErrorMessage,
    DateTime FailedAt) : IDomainEvent;
