using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.Collecting;
using Station.Application.IdGenerators;
using Station.Application.Security;
using Station.Application.Storage;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;

namespace Station.Application.Uploading;

public sealed class UploadService : IUploadService
{
    private readonly ILoopRepository<CollectTask> _taskRepo;
    private readonly ILoopRepository<CollectFile> _fileRepo;
    private readonly IRepository<UploadedFile> _uploadedFileRepo;
    private readonly IIdGenerator _idGenerator;
    private readonly IStorageService _storageService;
    private readonly CollectOptions _collectOptions;
    private readonly StorageOptions _storageConfig;
    private readonly ILogger<UploadService> _logger;
    private readonly IFileEncryptionService _fileEncryption;
    private readonly IDirectoryTemplateRenderer _renderer;

    public UploadService(
        ILoopRepository<CollectTask> taskRepo,
        ILoopRepository<CollectFile> fileRepo,
        IRepository<UploadedFile> uploadedFileRepo,
        IIdGenerator idGenerator,
        IStorageService storageService,
        IDirectoryTemplateRenderer renderer,
        IFileEncryptionService fileEncryption,
        CollectOptions collectOptions,
        StorageOptions storageConfig,
        ILogger<UploadService> logger)
    {
        _taskRepo = taskRepo;
        _fileRepo = fileRepo;
        _uploadedFileRepo = uploadedFileRepo;
        _storageService = storageService;
        _renderer = renderer;
        _fileEncryption = fileEncryption;
        _collectOptions = collectOptions;
        _storageConfig = storageConfig;
        _idGenerator = idGenerator;
        _logger = logger;
    }

    public async Task<UploadSummary> ProcessTaskAsync(long taskId)
    {
        var task = _taskRepo.GetById(taskId);
        if (task is null) return new UploadSummary(taskId, 0, 0, 0, false);
        if (task.Status != CollectTaskStatus.Completed) return new UploadSummary(taskId, 0, 0, 0, false);

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

        foreach (var file in pendingFiles)
        {
            var localPath = Path.Combine(_collectOptions.CacheDirectory, task.TaskNo, file.RelativePath!);
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

            var remoteDir = _renderer.Render(
                _storageConfig.DirectoryTemplate,
                _storageConfig.StationNo,
                task.CreatedAt,
                task.RecorderName,
                task.OperatorUserId,
                task.DeptId,
                Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant()) + "/" + file.FileName;

            // 根据上传语义选择流工厂
            Func<Stream>? streamFactory = null;
            long uploadSize = file.Size;

            if (file.LocalEncrypted)
            {
                if (_storageConfig.RemoteContentMode == RemoteContentMode.Plaintext)
                {
                    // 上传明文：解密流
                    streamFactory = () => _fileEncryption.CreateDecryptStream(localPath);
                    uploadSize = file.Size;   // 明文大小
                }
                else
                {
                    // 上传密文：直接读原文件
                    streamFactory = null;
                    uploadSize = file.EncryptedSize;   // 密文大小
                }
            }

            // 上传（expectedDigest 用 ContentDigest，始终是明文摘要）
            var result = await _storageService.UploadFileAsync(
                localPath,
                remoteDir,
                file.FileName,
                uploadSize,
                file.ContentDigest,   // 始终是明文摘要
                streamFactory,
                CancellationToken.None);

            if (result.Success)
            {
                file.SyncStatus = UploadStatus.Uploaded;
                file.UploadProgress = 1;
                file.RemotePath = result.PrimaryRemotePath;
                file.UploadError = null;
                _fileRepo.Update(file);

                await WriteUploadedFileAsync(task, file, result.PrimaryRemotePath);   // ← 同步改

                uploaded++;
                task.UploadedFiles++;
                task.UploadedBytes += file.Size;
                _taskRepo.Update(task);
                _logger.LogInformation("任务 {TaskNo} 文件 {FileName} 上传成功",
                    task.TaskNo, file.FileName);
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
                _logger.LogWarning("任务 {TaskNo} 文件 {FileName} 上传失败: {Error}",
                    task.TaskNo, file.FileName, result.ErrorMessage);
            }

            if (result.CircuitOpen)
            {
                circuitOpened = true;
                _logger.LogWarning("任务 {TaskNo} 上传中断：存储熔断已打开", task.TaskNo);
                break;
            }
        }

        var pendingCount = _fileRepo.Count(f => f.TaskId == taskId && f.SyncStatus == UploadStatus.Pending);
        task.SyncStatus = failed > 0 ? UploadStatus.Failed
            : (pendingCount == 0 ? UploadStatus.Uploaded : UploadStatus.Pending);
        task.UploadError = circuitOpened ? "存储熔断中"
            : (failed > 0 ? $"有 {failed} 个文件上传失败" : null);
        _taskRepo.Update(task);

        return new UploadSummary(taskId, uploaded, failed, pendingCount, circuitOpened);
    }

    /// <summary>上传成功后写 station_uploaded_file。</summary>
    private async Task WriteUploadedFileAsync(CollectTask task, CollectFile file, string? remotePath)
    {
        // 幂等：同一 LocalFileId 已存在则跳过
        if (await _uploadedFileRepo.IsAnyAsync(u => u.LocalFileId == file.Id))
            return;

        var uploaded = new UploadedFile
        {
            Id = _idGenerator.NextId(),   // 你项目里的 ID 生成器
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

        await _uploadedFileRepo.InsertAsync(uploaded);
    }

    private static FileKind ResolveKind(string? ext) => ext?.ToLowerInvariant() switch
    {
        ".mp4" or ".avi" or ".flv" or ".mov" => FileKind.Video,
        ".wav" or ".mp3" or ".aac" => FileKind.Audio,
        ".jpg" or ".jpeg" or ".bmp" or ".png" => FileKind.Image,
        ".log" or ".txt" => FileKind.Log,
        _ => FileKind.Other
    };

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

        if (failedFiles.Count > 0) await _fileRepo.UpdateRangeAsync(failedFiles);

        await ProcessTaskAsync(taskId);
        return failedFiles.Count;
    }

    public async Task<int> CountPendingUploadsAsync() =>
        await _fileRepo.CountAsync(f => f.SyncStatus == UploadStatus.Pending);
}
