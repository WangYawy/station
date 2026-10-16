using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

public sealed record CollectFileUploadStarted(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    long TaskId,
    string FileName,
    long UploadSize) : IDomainEvent;
