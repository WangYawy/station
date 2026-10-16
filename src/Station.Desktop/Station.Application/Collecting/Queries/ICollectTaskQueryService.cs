using Station.Application.Collecting.Dtos;

namespace Station.Application.Collecting;

public interface ICollectTaskQueryService
{
    Task<CollectTaskDto?> GetByIdAsync(long taskId, CancellationToken ct = default);

    Task<IReadOnlyList<CollectTaskDto>> GetRecentAsync(
        int count, CancellationToken ct = default);

    Task<IReadOnlyList<CollectTaskDto>> GetActiveAsync(CancellationToken ct = default);

    string GetMaxTaskNo(string prefix);

    Task<bool> ExistsTaskNoAsync(string taskNo, CancellationToken ct = default);
}
