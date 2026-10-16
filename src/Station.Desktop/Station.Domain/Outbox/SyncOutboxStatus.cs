namespace Station.Domain.Outbox;

public enum SyncOutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}
