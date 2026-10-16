namespace Station.Domain.Outbox;

public interface ISyncOutboxRepository
{
    Task<SyncOutboxItem?> GetByIdAsync(long id, CancellationToken ct = default);
    Task AddAsync(SyncOutboxItem item, CancellationToken ct = default);
    Task UpdateAsync(SyncOutboxItem item, CancellationToken ct = default);
}
