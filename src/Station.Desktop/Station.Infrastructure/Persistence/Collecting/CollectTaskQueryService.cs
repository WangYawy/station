using SqlSugar;
using Station.Application.Collecting;
using Station.Application.Collecting.Dtos;
using Station.Domain.Collecting;

namespace Station.Infrastructure.Persistence.Collecting;

public sealed class CollectTaskQueryService : ICollectTaskQueryService
{
    private readonly ISqlSugarClient _db;

    public CollectTaskQueryService(ISqlSugarClient db) => _db = db;

    public async Task<CollectTaskDto?> GetByIdAsync(long taskId, CancellationToken ct = default)
        => await _db.Queryable<CollectTaskPo>()
            .Where(x => x.Id == taskId)
            .Select(x => new CollectTaskDto(
                x.Id, x.TaskNo, x.RecorderName, x.RecorderSerial, x.Protocol,
                x.OperatorUserId, "", "", x.DeptId, "", x.Status, x.IsAuto,
                x.TotalFiles, x.CollectedFiles, x.SkippedFiles, x.FailedFiles,
                x.TotalBytes, x.CollectedBytes, x.SpeedBytesPerSecond,
                x.IsEmergency,
                x.SyncStatus, x.UploadedFiles, x.UploadedBytes, x.UploadError,
                x.StartedAt, x.CompletedAt, x.ErrorMessage))
            .FirstAsync(ct);

    public async Task<IReadOnlyList<CollectTaskDto>> GetRecentAsync(
        int count, CancellationToken ct = default)
    {
        if (count <= 0) return Array.Empty<CollectTaskDto>();

        return await _db.Queryable<CollectTaskPo>()
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .Take(count)
            .Select(x => new CollectTaskDto(
                x.Id, x.TaskNo, x.RecorderName, x.RecorderSerial, x.Protocol,
                x.OperatorUserId, "", "", x.DeptId, "", x.Status, x.IsAuto,
                x.TotalFiles, x.CollectedFiles, x.SkippedFiles, x.FailedFiles,
                x.TotalBytes, x.CollectedBytes, x.SpeedBytesPerSecond,
                x.IsEmergency,
                x.SyncStatus, x.UploadedFiles, x.UploadedBytes, x.UploadError,
                x.StartedAt, x.CompletedAt, x.ErrorMessage))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CollectTaskDto>> GetActiveAsync(
        CancellationToken ct = default)
    {
        return await _db.Queryable<CollectTaskPo>()
            .Where(x => x.Status == CollectTaskStatus.Scanning
                     || x.Status == CollectTaskStatus.Collecting
                     || x.Status == CollectTaskStatus.Paused)
            .OrderBy(x => x.CreatedAt, OrderByType.Desc)
            .Select(x => new CollectTaskDto(
                x.Id, x.TaskNo, x.RecorderName, x.RecorderSerial, x.Protocol,
                x.OperatorUserId, "", "", x.DeptId, "", x.Status, x.IsAuto,
                x.TotalFiles, x.CollectedFiles, x.SkippedFiles, x.FailedFiles,
                x.TotalBytes, x.CollectedBytes, x.SpeedBytesPerSecond,
                x.IsEmergency,
                x.SyncStatus, x.UploadedFiles, x.UploadedBytes, x.UploadError,
                x.StartedAt, x.CompletedAt, x.ErrorMessage))
            .ToListAsync(ct);
    }

    public string GetMaxTaskNo(string prefix)
    {
        return _db.Queryable<CollectTaskPo>().Where(x => x.TaskNo.StartsWith(prefix))
             .OrderByDescending(x => x.TaskNo)
             .Select(x => x.TaskNo)
             .First();
    }

    public Task<bool> ExistsTaskNoAsync(string taskNo, CancellationToken ct = default)
        => _db.Queryable<CollectTaskPo>().Where(x => x.TaskNo == taskNo).AnyAsync(ct);
}
