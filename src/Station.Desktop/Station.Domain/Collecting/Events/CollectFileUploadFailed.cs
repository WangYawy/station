using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

public sealed record CollectFileUploadFailed(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    long TaskId,
    string FileName,
    int RetryCount,
    string ErrorMessage) : IDomainEvent;
