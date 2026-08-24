using Microsoft.Extensions.Options;
using SqlSugar;
using Station.Application.Collecting;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.Db;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Storage;
using Station.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Station.Application.Uploading;

public sealed class UploadService : IUploadService
{
    private readonly IRepository<CollectTask> _tasks;
    private readonly IRepository<CollectFile> _files;
    private readonly IStorageTarget _target;
    private readonly StorageOptions _options;
    private readonly CollectOptions _collectOptions;
    private readonly IStorageCircuitBreaker _breaker;
    private readonly ISqlSugarFactory _sqlSugarFactory;
    private readonly DbOptions _dbOptions;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IRepository<CollectTask> tasks,
        IRepository<CollectFile> files,
        IStorageTarget target,
        StorageOptions options,
        CollectOptions collectOptions,
        IStorageCircuitBreaker breaker,
        ISqlSugarFactory sqlSugarFactory,
        DbOptions dbOptions,
        ILogger<UploadService> logger)
    {
        _tasks = tasks;
        _files = files;
        _target = target;
        _options = options;
        _collectOptions = collectOptions;
        _breaker = breaker;
        _sqlSugarFactory = sqlSugarFactory;
        _dbOptions = dbOptions;
        _logger = logger;
    }

    public async Task<UploadSummary> ProcessTaskAsync(long taskId)
    {
        // 上传循环使用独立长连接客户端 + 同步 DB 调用，避免 Kdbndp 异步连接并发问题
        using var loopClient = _sqlSugarFactory.CreateClient(_dbOptions, autoCloseConnection: false);

        var task = loopClient.Queryable<CollectTask>().InSingle(taskId);
        if (task is null)
        {
            return new UploadSummary(taskId, 0, 0, 0, false);
        }

        if (task.Status != CollectTaskStatus.Completed)
        {
            return new UploadSummary(taskId, 0, 0, 0, false);
        }

        if (_breaker.IsOpen)
        {
            task.UploadError = "存储熔断中，等待冷却";
            loopClient.Updateable(task).ExecuteCommand();
            return new UploadSummary(taskId, 0, 0, CountPending(loopClient, taskId), true);
        }

        var files = loopClient.Queryable<CollectFile>()
            .Where(f => f.TaskId == taskId && f.SyncStatus == UploadStatus.Pending)
            .OrderBy(f => f.Id)
            .ToList();

        var uploaded = 0;
        var failed = 0;
        var circuitOpened = false;

        foreach (var file in files)
        {
            if (_breaker.IsOpen)
            {
                circuitOpened = true;
                _logger.LogWarning("任务 {TaskNo} 上传中断：存储熔断已打开", task.TaskNo);
                break;
            }

            var localPath = Path.Combine(_collectOptions.CacheDirectory, task.TaskNo, file.RelativePath);
            if (!File.Exists(localPath))
            {
                file.SyncStatus = UploadStatus.Failed;
                file.UploadError = "本地缓存文件缺失";
                file.UploadRetryCount = _options.RetryCount;
                loopClient.Updateable(file).ExecuteCommand();
                failed++;
                continue;
            }

            file.SyncStatus = UploadStatus.Uploading;
            file.UploadProgress = 0;
            file.UploadError = null;
            loopClient.Updateable(file).ExecuteCommand();

            var remotePath = DirectoryTemplateRenderer.Render(
                _options.DirectoryTemplate,
                _options.StationNo,
                task.CreatedAt,
                task.RecorderName,
                task.OperatorUserId,
                task.DeptId,
                Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant()) + "/" + file.FileName;

            Exception? lastError = null;
            var success = false;
            for (var attempt = 1; attempt <= _options.RetryCount; attempt++)
            {
                try
                {
                    double lastPersisted = -1;
                    await _target.UploadAsync(
                        new UploadTargetFile(
                            localPath,
                            remotePath,
                            file.Size,
                            _collectOptions.EncryptCache
                                ? () => Sm4Crypto.CreateDecryptReader(localPath, Sm4KeyProvider.Default.GetKey())
                                : null),
                        async progress =>
                        {
                            file.UploadProgress = progress;
                            if (progress - lastPersisted >= 0.1)
                            {
                                lastPersisted = progress;
                                try
                                {
                                    loopClient.Updateable(file).ExecuteCommand();
                                }
                                catch
                                {
                                    // 进度落库失败不中断
                                }
                            }
                        },
                        CancellationToken.None);

                    var remoteSize = await _target.GetRemoteSizeAsync(remotePath, CancellationToken.None);
                    if (remoteSize != file.Size)
                    {
                        throw new IOException($"远端大小不一致：期望 {file.Size}，实际 {remoteSize}");
                    }

                    if (_options.VerifyRemoteSm3)
                    {
                        var localSm3 = file.Sm3 ?? ComputeLocalSm3(localPath);
                        var remoteSm3 = await _target.ComputeRemoteSm3Async(remotePath, CancellationToken.None);
                        if (!string.Equals(localSm3, remoteSm3, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException($"远端 SM3 不一致：本地 {localSm3}，远端 {remoteSm3}");
                        }
                    }

                    success = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _breaker.RecordFailure();
                    if (attempt < _options.RetryCount && _options.RetryIntervalSeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_options.RetryIntervalSeconds));
                    }
                }
            }

            if (success)
            {
                _breaker.RecordSuccess();
                file.SyncStatus = UploadStatus.Uploaded;
                file.UploadProgress = 1;
                file.UploadError = null;
                file.RemotePath = remotePath;
                loopClient.Updateable(file).ExecuteCommand();
                uploaded++;
                task.UploadedFiles++;
                task.UploadedBytes += file.Size;
                loopClient.Updateable(task).ExecuteCommand();
                _logger.LogInformation("任务 {TaskNo} 文件 {FileName} 上传成功 -> {RemotePath}", task.TaskNo, file.FileName, remotePath);
            }
            else
            {
                file.UploadRetryCount++;
                file.UploadError = lastError?.Message;
                file.SyncStatus = file.UploadRetryCount >= _options.RetryCount
                    ? UploadStatus.Failed
                    : UploadStatus.Pending;
                loopClient.Updateable(file).ExecuteCommand();
                failed++;
                _logger.LogWarning("任务 {TaskNo} 文件 {FileName} 上传失败（第 {Retry} 次）：{Error}",
                    task.TaskNo, file.FileName, file.UploadRetryCount, lastError?.Message);
            }
        }

        var pending = CountPending(loopClient, taskId);
        task.SyncStatus = failed > 0 ? UploadStatus.Failed : (pending == 0 ? UploadStatus.Uploaded : UploadStatus.Pending);
        task.UploadError = circuitOpened ? "存储熔断中" : (failed > 0 ? $"有 {failed} 个文件上传失败" : null);
        loopClient.Updateable(task).ExecuteCommand();

        return new UploadSummary(taskId, uploaded, failed, pending, circuitOpened || _breaker.IsOpen);
    }

    private string ComputeLocalSm3(string localPath)
    {
        using Stream stream = _collectOptions.EncryptCache
            ? (Stream)Sm4Crypto.CreateDecryptReader(localPath, Sm4KeyProvider.Default.GetKey())
            : File.OpenRead(localPath);
        return Sm3Checksum.Compute(stream);
    }

    public async Task<int> RetryFailedFilesAsync(long taskId)
    {
        var failedFiles = (await _files.GetListAsync(f =>
                f.TaskId == taskId && f.SyncStatus == UploadStatus.Failed))
            .ToList();
        foreach (var file in failedFiles)
        {
            file.SyncStatus = UploadStatus.Pending;
            file.UploadError = null;
        }

        if (failedFiles.Count > 0)
        {
            await _files.UpdateRangeAsync(failedFiles);
        }

        await ProcessTaskAsync(taskId);
        return failedFiles.Count;
    }

    public async Task<int> CountPendingUploadsAsync() =>
        await _files.CountAsync(f => f.SyncStatus == UploadStatus.Pending);

    private static int CountPending(ISqlSugarClient loopClient, long taskId) =>
        loopClient.Queryable<CollectFile>()
            .Count(f => f.TaskId == taskId && f.SyncStatus == UploadStatus.Pending);
}
