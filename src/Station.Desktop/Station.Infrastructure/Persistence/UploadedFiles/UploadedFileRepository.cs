using SqlSugar;
using Station.Domain.UploadedFiles;

namespace Station.Infrastructure.Persistence.UploadedFiles;

public sealed class UploadedFileRepository : IUploadedFileRepository
{
    private readonly ISqlSugarClient _db;
    public UploadedFileRepository(ISqlSugarClient db) => _db = db;

    public Task<bool> ExistsByLocalFileIdAsync(long localFileId, CancellationToken ct = default)
        => _db.Queryable<UploadedFilePo>().Where(x => x.LocalFileId == localFileId).AnyAsync(ct);

    public async Task AddAsync(UploadedFile file, CancellationToken ct = default)
        => await _db.Insertable(UploadedFileMapper.ToPo(file)).ExecuteCommandAsync(ct);
}
