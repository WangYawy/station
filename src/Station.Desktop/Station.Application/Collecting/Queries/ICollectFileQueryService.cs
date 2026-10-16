using Station.Application.Collecting.Dtos;
using Station.Data.Paging;

namespace Station.Application.Collecting;

public interface ICollectFileQueryService
{
    Task<PageResult<CollectFileDto>> PageAsync(
       CollectFilePageQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<CollectFileDto>> GetByTaskIdAsync(
        long taskId, CancellationToken ct = default);
    Task<string> GetMaxFileNoByTaskIdAsync(
        string taskNo, CancellationToken ct = default);

    Task<IReadOnlyList<CollectFileDto>> GetTodayStatsAsync(CancellationToken ct = default);
}
