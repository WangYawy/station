using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

public sealed record CollectFileUploaded(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    long TaskId,
    string FileName,
    long Size,
    string? RemotePath) : IDomainEvent;
