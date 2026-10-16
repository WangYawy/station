namespace Station.Domain.Collecting;

public interface ICollectTaskRepository
{
    Task<CollectTask?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<CollectTask>> GetActiveAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CollectTask>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<int> CountActiveEmergencyAsync(CancellationToken ct = default);

    Task AddAsync(CollectTask task, CancellationToken ct = default);
    Task UpdateAsync(CollectTask task, CancellationToken ct = default);
}
