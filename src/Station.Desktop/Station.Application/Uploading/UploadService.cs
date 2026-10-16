using Microsoft.Extensions.Logging;
using Station.Application.Collecting;
using Station.Application.Security;
using Station.Application.Storages;
using Station.Contracts;
using Station.Data.IdGeneration;
using Station.Data.Repositories;
using Station.Domain.Abstractions;
using Station.Domain.Collecting;
using Station.Domain.UploadedFiles;

namespace Station.Application.Uploading;

public sealed class UploadService : IUploadService
{
    // 长连接（后台批量上传，长连接避免与主线程竞争）
    private readonly ICollectTaskSyncRepository _taskSync;
    private readonly ICollectFileSyncRepository _fileSync;

    private readonly IUploadedFileRepository _uploadedFileRepo;
    private readonly IIdGenerator _idGenerator;
    private readonly IStorageService _storageService;
    private readonly CollectOptions _collectOptions;
    private readonly StorageOptions _storageConfig;
    private readonly IFileCryptoService _fileCrypto;
    private readonly IDirectoryTemplateRenderer _renderer;
    private readonly IDomainEventBus _eventBus;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        ICollectTaskSyncRepository taskSync,
        ICollectFileSyncRepository fileSync,
        IUploadedFileRepository uploadedFileRepo,
        IIdGenerator idGenerator,
        IStorageService storageService,
        IDirectoryTemplateRenderer renderer,
        IFileCryptoService fileCrypto,
        CollectOptions collectOptions,
        StorageOptions storageConfig,
        IDomainEventBus eventBus,
        ILogger<UploadService> logger)
    {
        _taskSync = taskSync;
        _fileSync = fileSync;
        _uploadedFileRepo = uploadedFileRepo;
        _idGenerator = idGenerator;
        _storageService = storageService;
        _renderer = renderer;
        _fileCrypto = fileCrypto;
        _collectOptions = collectOptions;
        _storageConfig = storageConfig;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<UploadSummary> ProcessTaskAsync(long taskId, CancellationToken ct = default)
    {
        var task = _taskSync.GetById(taskId);
        if (task is null) return new UploadSummary(taskId, 0, 0, 0, false);
        if (task.Status != CollectTaskStatus.Completed)
            return new UploadSummary(taskId, 0, 0, 0, false);

        var pendingFiles = _fileSync.GetPendingUploadsByTaskId(taskId);
        if (pendingFiles.Count == 0)
        {
            task.MarkUploaded(DateTime.Now);
            _taskSync.Update(task);
            await PublishAndClearAsync(task);
            return new UploadSummary(taskId, 0, 0, 0, false);
        }

        var uploaded = 0;
        var failed = 0;
        var circuitOpened = false;
        var now = DateTime.Now;

        foreach (var file in pendingFiles)
        {
            var localPath = Path.Combine(
                _collectOptions.CacheDirectory, task.TaskNo, file.RelativePath!);

            if (!File.Exists(localPath))
            {
                file.FailUpload("本地缓存文件缺失", _storageConfig.RetryCount, now);
                _fileSync.Update(file);
                await PublishAndClearAsync(file);   // ← await
                failed++;
                continue;
            }

            Func<Stream>? streamFactory = null;
            long uploadSize = file.Size;

            if (file.LocalEncrypted)
            {
                if (_storageConfig.RemoteContentMode == RemoteContentMode.Plaintext)
                {
                    streamFactory = () => _fileCrypto.OpenDecryptStream(localPath);
                    uploadSize = file.Size;
                }
                else
                {
                    streamFactory = null;
                    uploadSize = file.EncryptedSize;
                }
            }

            file.ResetForRetry(now);
            file.BeginUpload(uploadSize, now);
            _fileSync.Update(file);
            await PublishAndClearAsync(file);   // ← await

            var remoteDir = _renderer.Render(
                _storageConfig.DirectoryTemplate, _storageConfig.StationNo,
                task.CreatedAt, task.RecorderName, task.OperatorUserId, task.DeptId,
                Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant())
                + "/" + file.FileName;

            var result = await _storageService.UploadFileAsync(
                localPath, remoteDir, file.FileName,
                uploadSize, file.ContentDigest, streamFactory, ct);

            if (result.Success)
            {
                file.CompleteUpload(result.PrimaryRemotePath ?? string.Empty, DateTime.Now);
                _fileSync.Update(file);
                await PublishAndClearAsync(file);   // ← await

                await WriteUploadedFileAsync(task, file, result.PrimaryRemotePath);

                uploaded++;
                task.AddUploadedFile(file.Size, DateTime.Now);
                _taskSync.UpdateStateOnly(task);
                await PublishAndClearAsync(task);   // ← await（通常空）
            }
            else
            {
                file.FailUpload(result.ErrorMessage ?? "未知错误",
                    _storageConfig.RetryCount, DateTime.Now);
                _fileSync.Update(file);
                await PublishAndClearAsync(file);
                failed++;
            }

            if (result.CircuitOpen)
            {
                circuitOpened = true;
                break;
            }
        }

        var pendingCount = _fileSync.CountPendingUploadsByTaskId(taskId);
        if (failed > 0) task.MarkUploadFailed($"有 {failed} 个文件上传失败", DateTime.Now);
        else if (pendingCount == 0) task.MarkUploaded(DateTime.Now);
        if (circuitOpened) task.MarkUploadFailed("存储熔断中", DateTime.Now);

        _taskSync.UpdateStateOnly(task);
        await PublishAndClearAsync(task);   // ← await（任务级终态）

        return new UploadSummary(taskId, uploaded, failed, pendingCount, circuitOpened);
    }

    public async Task<int> RetryFailedFilesAsync(long taskId, CancellationToken ct = default)
    {
        var failedFiles = _fileSync
            .GetByTaskId(taskId)
            .Where(f => f.SyncStatus == UploadStatus.Failed)
            .ToList();

        if (failedFiles.Count > 0)
        {
            var now = DateTime.Now;
            foreach (var f in failedFiles)
                f.ResetForRetry(now);

            _fileSync.UpdateRange(failedFiles);
            foreach (var f in failedFiles) await PublishAndClearAsync(f);
        }

        await ProcessTaskAsync(taskId, ct);
        return failedFiles.Count;
    }

    public Task<int> CountPendingUploadsAsync(CancellationToken ct = default)
        => Task.FromResult(_fileSync.CountPendingUploads());

    // =========================================================
    // 内部
    // =========================================================

    private async Task WriteUploadedFileAsync(CollectTask task, CollectFile file, string? remotePath)
    {
        if (await _uploadedFileRepo.ExistsByLocalFileIdAsync(file.Id))
            return;

        var uploaded = new UploadedFile
        {
            Id = _idGenerator.NewId(),
            LocalFileId = file.Id,
            FileNo = file.FileName,
            FileName = file.FileName,
            Size = file.Size,
            Kind = ResolveKind(file.Extension),
            ContentDigest = file.ContentDigest ?? string.Empty,
            DigestAlgorithm = file.DigestAlgorithm ?? "SM3",
            Signature = file.Signature,
            SignatureAlgorithm = file.SignatureAlgorithm,
            RemoteContentMode = _storageConfig.RemoteContentMode.ToString(),
            CollectedAt = file.CollectedAt ?? DateTime.Now,
            OriginalTime = file.OriginalModifiedAt,
            UserId = task.OperatorUserId,
            DeptId = task.DeptId,
            Status = Station.Contracts.TaskStatus.Completed
        };

        await _uploadedFileRepo.AddAsync(uploaded);
    }

    private static FileKind ResolveKind(string? ext) => ext?.ToLowerInvariant() switch
    {
        ".mp4" or ".avi" or ".flv" or ".mov" => FileKind.Video,
        ".wav" or ".mp3" or ".aac" => FileKind.Audio,
        ".jpg" or ".jpeg" or ".bmp" or ".png" => FileKind.Image,
        ".log" or ".txt" => FileKind.Log,
        _ => FileKind.Other
    };

    private async Task PublishAndClearAsync(CollectTask task, CancellationToken ct = default)
    {
        if (task.DomainEvents.Count == 0) return;
        var events = task.DomainEvents.ToList();
        task.ClearDomainEvents();
        await _eventBus.PublishRangeAsync(events, ct);
    }

    private async Task PublishAndClearAsync(CollectFile file, CancellationToken ct = default)
    {
        if (file.DomainEvents.Count == 0) return;
        var events = file.DomainEvents.ToList();
        file.ClearDomainEvents();
        await _eventBus.PublishRangeAsync(events, ct);
    }
}
