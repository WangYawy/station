using Station.Domain.Outbox;

namespace Station.Infrastructure.Persistence.Outbox;

internal static class SyncOutboxMapper
{
    public static SyncOutboxItem ToDomain(SyncOutboxPo po)
        => SyncOutboxItem.Restore(
            po.Id, po.Topic, po.PayloadJson, (SyncOutboxStatus)po.Status,
            po.RetryCount, po.NextRetryAt, po.SentAt, po.Error, po.CreatedAt);

    public static SyncOutboxPo ToPo(SyncOutboxItem item) => new()
    {
        Id = item.Id,
        Topic = item.Topic,
        PayloadJson = item.PayloadJson,
        Status = (int)item.Status,
        RetryCount = item.RetryCount,
        NextRetryAt = item.NextRetryAt,
        SentAt = item.SentAt,
        Error = item.Error,
        CreatedAt = item.CreatedAt
    };
}
