using SqlSugar;
using Station.Application.Collecting;
using Station.Application.Collecting.Dtos;
using Station.Data.Paging;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

public sealed class CollectFileQueryService : ICollectFileQueryService
{
    private readonly ISqlSugarClient _db;

    public CollectFileQueryService(ISqlSugarClient db) => _db = db;

    public async Task<PageResult<CollectFileDto>> PageAsync(CollectFilePageQuery query, CancellationToken ct = default)
    {
        //return await _db.Queryable<CollectFilePo>()
        //   .Select(x => new CollectFileDto(
        //        x.Id, x.TaskId, x.FileName, x.Extension, x.Size,
        //        x.Status, x.Progress, x.SpeedBytesPerSecond,
        //        x.ErrorMessage, x.CollectedAt, x.RemotePath, x.UploadError))
        //    .ToListAsync(ct);
        return null;
    }

    public async Task<IReadOnlyList<CollectFileDto>> GetByTaskIdAsync(
        long taskId, CancellationToken ct = default)
        => await _db.Queryable<CollectFilePo>()
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.Id)
            .Select(x => new CollectFileDto(
                x.Id, x.TaskId, x.FileName, x.Extension, x.Size,
                x.Status, x.Progress, x.SpeedBytesPerSecond,
                x.ErrorMessage, x.CollectedAt, x.RemotePath, x.UploadError))
            .ToListAsync(ct);

    public async Task<string> GetMaxFileNoByTaskIdAsync(
        string taskNo, CancellationToken ct = default)
        => await _db.Queryable<CollectFilePo>()
           .Where(x => x.TaskNo == taskNo)
           .OrderByDescending(x => x.FileNo)
           .Select(x => x.FileNo)
           .FirstAsync(ct);

    public async Task<IReadOnlyList<CollectFileDto>> GetTodayStatsAsync(CancellationToken ct = default)
        => await _db.Queryable<CollectFilePo>()
        .Where(x => x.Status == CollectFileStatus.Completed && x.CollectedAt >= DateTime.Today)
        .Select(x => new CollectFileDto(
            x.Id, x.TaskId, x.FileName, x.Extension, x.Size,
            x.Status, x.Progress, x.SpeedBytesPerSecond,
            x.ErrorMessage, x.CollectedAt, x.RemotePath, x.UploadError))
        .ToListAsync(ct);
}
