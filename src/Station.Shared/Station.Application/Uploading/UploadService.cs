using SqlSugar;
using Station.Application.Collecting;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;
using Station.Domain.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Storage;

namespace Station.Application.Uploading;

public sealed class UploadService : IUploadService
{
    private readonly ILoopRepository<CollectTask> _taskRepo;
    private readonly ILoopRepository<CollectFile> _fileRepo;
    private readonly IStorageService _storageService;
    private readonly CollectOptions _collectOptions;
    private readonly IStorageConfiguration _storageConfig;
    private readonly ILogger<UploadService> _logger;
    private readonly IFileEncryptionService _decryptionService;

    public UploadService(
        ILoopRepository<CollectTask> taskRepo,
        ILoopRepository<CollectFile> fileRepo,
        IStorageService storageService,
        //IDirectoryTemplateRenderer renderer,
        IFileEncryptionService decryptionService,
        CollectOptions collectOptions,
        IStorageConfiguration storageConfig,
        ILogger<UploadService> logger)
    {
        _taskRepo = taskRepo;
        _fileRepo = fileRepo;
        _storageService = storageService;
        //_renderer = renderer;
        _collectOptions = collectOptions;
        _decryptionService = decryptionService;
        _storageConfig = storageConfig;
        _logger = logger;
    }

    public async Task<UploadSummary> ProcessTaskAsync(long taskId)
    {
        // 上传循环使用独立长连接客户端 + 同步 DB 调用，避免 Kdbndp 异步连接并发问题
        //using var scope = _scopeFactory.CreateScope();
        //var taskRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<CollectTask>>();
        //var fileRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<CollectFile>>();
        // 1. 获取任务
        var task = _taskRepo.GetById(taskId);
        if (task is null)
        {
            return new UploadSummary(taskId, 0, 0, 0, false);
        }

        if (task.Status != CollectTaskStatus.Completed)
        {
            return new UploadSummary(taskId, 0, 0, 0, false);
        }

        //if (_breaker.IsOpen)
        //{
        //    task.UploadError = "存储熔断中，等待冷却";
        //    taskRepo.Update(task);
        //    return new UploadSummary(taskId, 0, 0, CountPending(fileRepo, taskId), true);
        //}

        // 2. 获取待上传文件列表
        var pendingFiles = _fileRepo.GetList(f => f.TaskId == taskId && f.SyncStatus == UploadStatus.Pending);
        if (!pendingFiles.Any())
        {
            task.SyncStatus = UploadStatus.Uploaded;
            _taskRepo.Update(task);
            return new UploadSummary(taskId, 0, 0, 0, false);
        }

        var uploaded = 0;
        var failed = 0;
        var circuitOpened = false;
        // 3. 循环处理每个文件
        foreach (var file in pendingFiles)
        {
            //if (_breaker.IsOpen)
            //{
            //    circuitOpened = true;
            //    _logger.LogWarning("任务 {TaskNo} 上传中断：存储熔断已打开", task.TaskNo);
            //    break;
            //}

            var localPath = Path.Combine(_collectOptions.CacheDirectory, task.TaskNo, file.RelativePath);
            if (!File.Exists(localPath))
            {
                file.SyncStatus = UploadStatus.Failed;
                file.UploadError = "本地缓存文件缺失";
                file.UploadRetryCount = _storageConfig.RetryCount;
                _fileRepo.Update(file);
                failed++;
                continue;
            }

            file.SyncStatus = UploadStatus.Uploading;
            file.UploadProgress = 0;
            file.UploadError = null;
            _fileRepo.Update(file);

            // 渲染远程目录
            var remoteDir = DirectoryTemplateRenderer.Render(
                _storageConfig.DirectoryTemplate,
                _storageConfig.StationNo,
                task.CreatedAt,
                task.RecorderName,
                task.OperatorUserId,
                task.DeptId,
                Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant()) + "/" + file.FileName;

            // 调用存储服务
            var result = await _storageService.UploadFileAsync(
                localPath,
                remoteDir,
                file.FileName,
                file.Size,
                file.Sm3,
                _collectOptions.EncryptCache
                    ? () => _decryptionService.CreateDecryptStream(localPath) // 需注入 IFileDecryptionService
                    : null,
                CancellationToken.None);

            // 更新文件状态
            if (result.Success)
            {
                file.SyncStatus = UploadStatus.Uploaded;
                file.UploadProgress = 1;
                file.RemotePath = result.RemotePath;
                file.UploadError = null;
                _fileRepo.Update(file);
                uploaded++;
                task.UploadedFiles++;
                task.UploadedBytes += file.Size;
                _taskRepo.Update(task);
                _logger.LogInformation("任务 {TaskNo} 文件 {FileName} 上传成功", task.TaskNo, file.FileName);
            }
            else
            {
                file.UploadRetryCount++;
                file.UploadError = result.ErrorMessage;
                file.SyncStatus = file.UploadRetryCount >= _storageConfig.RetryCount
                    ? UploadStatus.Failed
                    : UploadStatus.Pending;
                _fileRepo.Update(file);
                failed++;
                _logger.LogWarning("任务 {TaskNo} 文件 {FileName} 上传失败: {Error}", task.TaskNo, file.FileName, result.ErrorMessage);
            }

            if (result.CircuitBreakerOpen)
            {
                circuitOpened = true;
                _logger.LogWarning("任务 {TaskNo} 上传中断：存储熔断已打开", task.TaskNo);
                break;
            }

        }
        // 4. 更新任务总状态
        var pendingCount = _fileRepo.Count(f => f.TaskId == taskId && f.SyncStatus == UploadStatus.Pending);
        task.SyncStatus = failed > 0 ? UploadStatus.Failed : (pendingCount == 0 ? UploadStatus.Uploaded : UploadStatus.Pending);
        task.UploadError = circuitOpened ? "存储熔断中" : (failed > 0 ? $"有 {failed} 个文件上传失败" : null);
        _taskRepo.Update(task);

        return new UploadSummary(taskId, uploaded, failed, pendingCount, circuitOpened);
    }

    public async Task<int> RetryFailedFilesAsync(long taskId)
    {
        var failedFiles = (await _fileRepo.GetListAsync(f =>
                f.TaskId == taskId && f.SyncStatus == UploadStatus.Failed))
            .ToList();
        foreach (var file in failedFiles)
        {
            file.SyncStatus = UploadStatus.Pending;
            file.UploadError = null;
        }

        if (failedFiles.Count > 0)
        {
            await _fileRepo.UpdateRangeAsync(failedFiles);
        }

        await ProcessTaskAsync(taskId);
        return failedFiles.Count;
    }

    public async Task<int> CountPendingUploadsAsync() =>
        await _fileRepo.CountAsync(f => f.SyncStatus == UploadStatus.Pending);

    private static int CountPending(ILoopRepository<CollectFile> fileRepo, long taskId) =>
        fileRepo.Count(f => f.TaskId == taskId && f.SyncStatus == UploadStatus.Pending);
}
