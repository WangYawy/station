using Station.Domain.Abstractions;

namespace Station.Domain.Collecting.Events;

/// <summary>单个文件采集完成（本地复制 + 摘要 + 签名 + 加密完成）。</summary>
public sealed record CollectFileCollected(
    Guid EventId,
    DateTime OccurredAt,
    long AggregateId,
    string AggregateType,
    long TaskId,
    string FileName,
    long Size,
    string? ContentDigest,
    string? DigestAlgorithm) : IDomainEvent;
