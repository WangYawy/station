using System.Text.Json;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Reporting;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using TaskStatus = Station.Contracts.TaskStatus;

namespace Station.Application.Collecting;

public sealed class FileLedgerService : IFileLedgerService
{
    private readonly IRepository<VideoFile> _ledger;
    private readonly IRepository<User> _users;
    private readonly IRepository<Dept> _depts;
    private readonly ISyncOutboxService _outbox;
    private readonly IStationContext _stationContext;
    private readonly IIdGenerator _idGenerator;
    private readonly PlatformOptions _platformOptions;
    private readonly CollectOptions _collectOptions;
    private readonly ISqlSugarFactory _sqlSugarFactory;
    private readonly DbOptions _dbOptions;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public FileLedgerService(
        IRepository<VideoFile> ledger,
        IRepository<User> users,
        IRepository<Dept> depts,
        ISyncOutboxService outbox,
        IStationContext stationContext,
        IIdGenerator idGenerator,
        IOptions<PlatformOptions> platformOptions,
        IOptions<CollectOptions> collectOptions,
        ISqlSugarFactory sqlSugarFactory,
        DbOptions dbOptions)
    {
        _ledger = ledger;
        _users = users;
        _depts = depts;
        _outbox = outbox;
        _stationContext = stationContext;
        _idGenerator = idGenerator;
        _platformOptions = platformOptions.Value;
        _collectOptions = collectOptions.Value;
        _sqlSugarFactory = sqlSugarFactory;
        _dbOptions = dbOptions;
    }

    public async Task<int> ProcessCompletedTaskAsync(long taskId)
    {
        using var client = _sqlSugarFactory.CreateClient(_dbOptions, autoCloseConnection: false);
        var task = client.Queryable<CollectTask>().InSingle(taskId);
        if (task is null || task.Status != CollectTaskStatus.Completed || task.LedgeredAt is not null)
        {
            return 0;
        }

        var files = client.Queryable<CollectFile>()
            .Where(f => f.TaskId == taskId && f.Status == CollectFileStatus.Completed)
            .OrderBy(f => f.Id)
            .ToList();

        var user = task.OperatorUserId is null ? null : await _users.GetByIdAsync(task.OperatorUserId.Value);
        var dept = task.DeptId is null ? null : await _depts.GetByIdAsync(task.DeptId.Value);

        foreach (var file in files)
        {
            file.Sm3 ??= Sm3Checksum.ComputeFile(
                Path.Combine(_collectOptions.CacheDirectory, task.TaskNo, file.RelativePath));
            file.FileNo = FileNo.Create(_platformOptions.StationCode, file.Id);
            client.Updateable(file).ExecuteCommand();

            if (!await _ledger.IsAnyAsync(v => v.FileNo == file.FileNo))
            {
                await _ledger.InsertAsync(new VideoFile
                {
                    Id = _idGenerator.NextId(),
                    LocalFileId = file.Id,
                    FileNo = file.FileNo,
                    FileName = file.FileName,
                    Size = file.Size,
                    Kind = FileKindMapper.Map(file.FileName),
                    Sm3 = file.Sm3,
                    CollectedAt = task.StartedAt ?? task.CreatedAt,
                    OriginalTime = file.OriginalModifiedAt,
                    UserId = task.OperatorUserId,
                    DeptId = task.DeptId,
                    Status = TaskStatus.Completed
                });
            }

            if (_stationContext.StationId is { } stationId)
            {
                var report = new FileMetadataReport
                {
                    StationId = stationId,
                    LocalFileId = file.Id,
                    FileNo = file.FileNo,
                    FileName = file.FileName,
                    Size = file.Size,
                    Kind = FileKindMapper.Map(file.FileName),
                    Sm3 = file.Sm3,
                    CollectedAt = task.StartedAt ?? task.CreatedAt,
                    OriginalTime = file.OriginalModifiedAt,
                    UserNo = user?.UserNo,
                    DeptCode = dept?.Code,
                    RecorderSerial = task.RecorderSerial ?? task.RecorderName,
                    StorageLocation = file.RemotePath
                };
                await _outbox.EnqueueAsync("file-metadata", JsonSerializer.Serialize(report, _json));
            }
        }

        task.LedgeredAt = DateTime.Now;
        client.Updateable(task).ExecuteCommand();
        return files.Count;
    }
}
