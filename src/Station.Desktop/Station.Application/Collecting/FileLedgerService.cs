using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.IdGenerators;
using Station.Application.PlatformSync;
using Station.Contracts;
using Station.Contracts.Reporting;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;
using Station.Domain.Security;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly IFileChecksumService _checksumService;
    private readonly IFileEncryptionService _encryptionService;


    public FileLedgerService(
        IRepository<VideoFile> ledger,
        IRepository<User> users,
        IRepository<Dept> depts,
        ISyncOutboxService outbox,
        IStationContext stationContext,
        IIdGenerator idGenerator,
        IOptions<PlatformOptions> platformOptions,
        CollectOptions collectOptions,
        IServiceScopeFactory scopeFactory,
        IFileEncryptionService encryptionService,
        IFileChecksumService checksumService
        )
    {
        _ledger = ledger;
        _users = users;
        _depts = depts;
        _outbox = outbox;
        _stationContext = stationContext;
        _idGenerator = idGenerator;
        _platformOptions = platformOptions.Value;
        _collectOptions = collectOptions;
        _scopeFactory = scopeFactory;
        _checksumService = checksumService;
        _encryptionService = encryptionService;
    }

    public async Task<int> ProcessCompletedTaskAsync(long taskId)
    {
        using var scope = _scopeFactory.CreateScope();
        // 2. 从作用域解析需要的泛型 LoopClient（它们共享同一个 ISqlSugarClient）
        var taskLoop = scope.ServiceProvider.GetRequiredService<ILoopRepository<CollectTask>>();
        var fileLoop = scope.ServiceProvider.GetRequiredService<ILoopRepository<CollectFile>>();

        var task = taskLoop.GetById(taskId);
        if (task is null || task.Status != CollectTaskStatus.Completed || task.LedgeredAt is not null)
        {
            return 0;
        }

        var files = fileLoop.GetList(f => f.TaskId == taskId && f.Status == CollectFileStatus.Completed)
            .OrderBy(f => f.Id)
            .ToList();

        var user = task.OperatorUserId is null ? null : await _users.GetByIdAsync(task.OperatorUserId.Value);
        var dept = task.DeptId is null ? null : await _depts.GetByIdAsync(task.DeptId.Value);

        foreach (var file in files)
        {
            var cachePath = Path.Combine(_collectOptions.CacheDirectory, task.TaskNo, file.RelativePath);
            file.Sm3 ??= _collectOptions.EncryptCache
                ? _checksumService.Compute(_encryptionService.CreateDecryptStream(cachePath))
                : _checksumService.ComputeFile(cachePath);
            file.FileNo = FileNo.Create(_platformOptions.StationCode, file.Id);
            fileLoop.Update(file);

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
        taskLoop.Update(task);
        return files.Count;
    }
}
