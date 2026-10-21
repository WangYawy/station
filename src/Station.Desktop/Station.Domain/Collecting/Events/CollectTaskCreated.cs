using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

public sealed record CollectTaskCreated(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    string TaskNo,
    string? PhysicalPortId,
    string RecorderSerial,
    string RecorderName,
    long? OperatorUserId,
    long? DeptId,
    bool IsAuto,
    bool IsEmergency) : IDomainEvent;
