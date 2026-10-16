using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Station.Application.Collecting.Dtos;
using Station.Application.Licensing;
using Station.Application.Notifications;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Application.Storages;
using Station.Crypto;
using Station.Data.IdGeneration;
using Station.Domain.Abstractions;
using Station.Domain.Collecting;

namespace Station.Application.Collecting;

/// <summary>采集任务应用服务（聚合仓储 + 领域事件）。</summary>
public sealed class CollectTaskService : ICollectTaskService
{
    private sealed class TaskControl
    {
        public CancellationTokenSource Cts { get; } = new();
        public volatile bool PauseRequested;
        public volatile CollectTaskStatus FinalStatus = CollectTaskStatus.Completed;
        public long CurrentFileId;
    }

    // 短连接（前台操作）
    private readonly ICollectTaskRepository _tasks;
    private readonly ICollectFileRepository _files;

    // 读侧
    private readonly ICollectTaskQueryService _taskQuery;

    private readonly IIdGenerator _idGenerator;
    private readonly ICollectSourceProvider _sources;
    private readonly CollectOptions _options;
    private readonly ICollectControl _collectControl;
    private readonly ILicenseService _licenseService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileCryptoService _fileCrypto;
    private readonly StorageOptions _storageConfig;
    private readonly IUiNotificationService _ui;
    private readonly IStationInfoService _station;
    private readonly ICollectTaskNoGenerator _taskNoGen;
    private readonly ICollectFileNoGenerator _fileNoGen;
    private readonly IDomainEventBus _eventBus;

    private readonly ConcurrentDictionary<long, TaskControl> _controls = new();
    private readonly ILogger<CollectTaskService> _logger;

    public CollectTaskService(
        ICollectTaskRepository tasks,
        ICollectFileRepository files,
        ICollectTaskQueryService taskQuery,
        IIdGenerator idGenerator,
        ICollectSourceProvider sources,
        CollectOptions options,
        ICollectControl collectControl,
        ILicenseService licenseService,
        IServiceScopeFactory scopeFactory,
        IFileCryptoService fileCrypto,
        StorageOptions storageConfig,
        IUiNotificationService ui,
        IStationInfoService station,
        ICollectTaskNoGenerator taskNoGen,
        ICollectFileNoGenerator fileNoGen,
        IDomainEventBus eventBus,
        ILogger<CollectTaskService> logger)
    {
        _tasks = tasks;
        _files = files;
        _taskQuery = taskQuery;
        _idGenerator = idGenerator;
        _sources = sources;
        _options = options;
        _collectControl = collectControl;
        _licenseService = licenseService;
        _scopeFactory = scopeFactory;
        _fileCrypto = fileCrypto;
        _storageConfig = storageConfig;
        _ui = ui;
        _station = station;
        _taskNoGen = taskNoGen;
        _fileNoGen = fileNoGen;
        _eventBus = eventBus;
        _logger = logger;
    }

    // =========================================================
    // 创建 / 启动
    // =========================================================

    public async Task<CollectTaskDto> CreateTaskAsync(
        CollectDeviceInfo device, bool isAuto, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var id = _idGenerator.NewId();
        var taskNo = await _taskNoGen.NextAsync(_station.Current.StationCode, device.Serial!, now, ct);

        var task = CollectTask.Create(
            id, taskNo,
            device.Name, device.Serial, device.Protocol, device.RootPath,
            device.UserId, device.DeptId, isAuto, now);

        await _tasks.AddAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        _ui.PublishTaskStatus(task.Id, task.Status, task.IsEmergency);

        _logger.LogInformation(
            "采集任务创建 {TaskNo}：记录仪 {Recorder}（{Protocol}，{Mode}）",
            task.TaskNo, device.Name, device.Protocol, isAuto ? "自动" : "手动");

        if (isAuto && _options.AutoCollectOnConnect)
        {
            return await StartAsync(task.Id, ct);
        }

        return ToDto(task);
    }

    public async Task<CollectTaskDto> StartAsync(long taskId, CancellationToken ct = default)
    {
        if (!await _licenseService.IsValidNowAsync())
            throw new InvalidOperationException("授权不可用，无法采集");

        if (!_collectControl.CollectingEnabled)
            throw new InvalidOperationException($"采集已停止：{_collectControl.StoppedReason ?? "远程指令"}");

        var task = await _tasks.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"任务 {taskId} 不存在");

        if (_controls.ContainsKey(taskId))
            throw new InvalidOperationException("任务正在运行中");

        // ---- 1. 扫描阶段 ----
        task.BeginScanning(DateTime.Now);
        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        _ui.PublishTaskStatus(taskId, task.Status, task.IsEmergency); // 立即通知UI

        // ---- 2. 扫描设备文件 ----
        var device = new CollectDeviceInfo(
            task.RecorderName, task.RecorderSerial, task.Protocol,
            RootPath: task.SourceRoot);
        var collectSource = _sources.GetFor(device.Protocol);
        var sources = await collectSource.ScanAsync(device, ct);
        var accepted = sources.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();

        // ---- 3. 已采集指纹（合并 N+1） ----
        HashSet<string> completedFingerprints;
        if (_options.SkipCollected && accepted.Count > 0)
        {
            var fingerprints = accepted
                .Select(f => f.Fingerprint)
                .Where(fp => !string.IsNullOrEmpty(fp))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            completedFingerprints = await _files.GetCompletedFingerprintsAsync(fingerprints, ct);
        }
        else
        {
            completedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        }

        // ---- 4. 批量构造聚合 ----
        using var fileCtx = await _fileNoGen.CreateContextAsync(task.TaskNo, ct);
        var toInsert = new List<CollectFile>(accepted.Count);
        var skipped = 0;

        foreach (var source in accepted)
        {
            var already = _options.SkipCollected && completedFingerprints.Contains(source.Fingerprint);
            if (already) skipped++;

            var file = CollectFile.Create(
                _idGenerator.NewId(), taskId, task.TaskNo, fileCtx.Next(),
                source.FileName,
                Path.GetExtension(source.FileName).ToLowerInvariant(),
                source.RelativePath, source.Size, source.Fingerprint,
                source.ModifiedAt, already, DateTime.Now);

            toInsert.Add(file);
        }

        if (toInsert.Count > 0)
            await _files.AddRangeAsync(toInsert, ct);

        _logger.LogInformation(
            "设备 {Recorder} 任务 {TaskNo} 扫描完成：{Total} 个文件（跳过 {Skipped}）",
            task.RecorderSerial, task.TaskNo, accepted.Count, skipped);

        // ---- 5. 进入采集状态（聚合方法同时设统计字段） ----
        task.BeginCollecting(
            accepted.Count, skipped,
            accepted.Sum(f => f.Size), DateTime.Now);

        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        _ui.PublishTaskStatus(taskId, task.Status, task.IsEmergency);   // UI 立即

        // ---- 6. 后台采集线程 ----
        var control = new TaskControl();
        _controls[taskId] = control;

        _ = Task.Factory.StartNew(
            () =>
            {
                try { RunCollectAsync(taskId, control).GetAwaiter().GetResult(); }
                catch (Exception ex) { _logger.LogError(ex, "采集线程未捕获异常 {TaskId}", taskId); }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return ToDto(task);
    }

    // =========================================================
    // 暂停 / 恢复 / 取消 / 中断
    // =========================================================

    public Task PauseAsync(long taskId, CancellationToken ct = default)
    {
        if (_controls.TryGetValue(taskId, out var control))
        {
            control.PauseRequested = true;
            control.Cts.Cancel();
            _logger.LogInformation("采集任务 {TaskId} 暂停请求", taskId);
        }
        return Task.CompletedTask;
    }

    public async Task ResumeAsync(long taskId, CancellationToken ct = default)
    {
        var task = await _tasks.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"任务 {taskId} 不存在");

        if (_controls.ContainsKey(taskId))
            throw new InvalidOperationException("任务正在运行中");

        task.Resume(DateTime.Now);
        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        _ui.PublishTaskStatus(taskId, task.Status, task.IsEmergency);

        var control = new TaskControl();
        _controls[taskId] = control;

        _ = Task.Factory.StartNew(
            () =>
            {
                try { RunCollectAsync(taskId, control).GetAwaiter().GetResult(); }
                catch (Exception ex) { _logger.LogError(ex, "采集线程未捕获异常 {TaskId}", taskId); }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        _logger.LogInformation("采集任务 {TaskId} 恢复", taskId);
    }

    public Task CancelAsync(long taskId, CancellationToken ct = default)
        => SetFinalAsync(taskId, CollectTaskStatus.Canceled, null, ct);

    public Task InterruptAsync(long taskId, string reason, CancellationToken ct = default)
        => SetFinalAsync(taskId, CollectTaskStatus.Interrupted, reason, ct);

    public async Task<bool> SetEmergencyAsync(
        long taskId, bool isEmergency, CancellationToken ct = default)
    {
        var task = await _tasks.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"任务 {taskId} 不存在");

        if (isEmergency && !task.IsEmergency)
        {
            var current = await _tasks.CountActiveEmergencyAsync(ct);
            if (current >= _options.MaxEmergencyTasks)
                return false;
        }

        task.MarkEmergency(isEmergency, DateTime.Now);
        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        _ui.PublishTaskStatus(taskId, task.Status, task.IsEmergency);

        _logger.LogInformation("任务 {TaskId} 紧急优先标记 -> {Value}", taskId, isEmergency);
        return true;
    }

    // =========================================================
    // 读（走 QueryService）
    // =========================================================

    public Task<CollectTaskDto?> GetTaskAsync(long taskId, CancellationToken ct = default)
        => _taskQuery.GetByIdAsync(taskId, ct);

    public async Task<IReadOnlyList<CollectTaskDto>> GetTasksAsync(
        int count, CancellationToken ct = default)
        => await _taskQuery.GetRecentAsync(count, ct);

    public async Task<IReadOnlyList<CollectTaskDto>> GetActiveTasksAsync(
        CancellationToken ct = default)
        => await _taskQuery.GetActiveAsync(ct);

    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(
        long taskId, CancellationToken ct = default)
    {
        var files = await _files.GetByTaskIdAsync(taskId, ct);
        return files.OrderBy(f => f.Id).Select(ToDto).ToList();
    }

    // =========================================================
    // 内部：收尾
    // =========================================================

    private async Task SetFinalAsync(
        long taskId, CollectTaskStatus finalStatus, string? reason,
        CancellationToken ct = default)
    {
        if (_controls.TryGetValue(taskId, out var control))
        {
            control.FinalStatus = finalStatus;

            if (!string.IsNullOrEmpty(reason))
            {
                var task = await _tasks.GetByIdAsync(taskId, ct);
                if (task is not null)
                {
                    task.AppendErrorMessage(reason);
                    await _tasks.UpdateAsync(task, ct);
                    // 错误信息变化不视为状态变化，无需发布领域事件
                    task.ClearDomainEvents();
                }
            }

            control.Cts.Cancel();
            return;
        }

        // 无活动控制块：直接对空闲任务执行终态
        var idle = await _tasks.GetByIdAsync(taskId, ct);
        if (idle is null) return;

        var now = DateTime.Now;
        switch (finalStatus)
        {
            case CollectTaskStatus.Canceled:
                idle.Cancel(reason, now);
                break;
            case CollectTaskStatus.Interrupted:
                idle.Interrupt(reason ?? "任务中断", now);
                break;
            default:
                idle.Fail(reason ?? "任务失败", now);
                break;
        }

        await _tasks.UpdateAsync(idle, ct);
        await PublishAndClearAsync(idle, ct);
        _ui.PublishTaskStatus(taskId, idle.Status, idle.IsEmergency);

        var pending = await _files.GetPendingByTaskIdAsync(taskId, ct);
        if (pending.Count > 0)
        {
            foreach (var f in pending)
            {
                if (finalStatus == CollectTaskStatus.Interrupted)
                    f.Interrupt("设备断开，采集中断", now);
                else
                    f.Cancel("任务已取消", now);
            }
            await _files.UpdateRangeAsync(pending, ct);
            await PublishFilesAsync(pending, ct);
        }
    }

    // =========================================================
    // 内部：后台采集主循环（长连接）
    // =========================================================

    private async Task RunCollectAsync(long taskId, TaskControl control)
    {
        using var scope = _scopeFactory.CreateScope();
        var taskSync = scope.ServiceProvider.GetRequiredService<ICollectTaskSyncRepository>();
        var fileSync = scope.ServiceProvider.GetRequiredService<ICollectFileSyncRepository>();

        CollectTask? task = null;
        try
        {
            var ct = control.Cts.Token;
            task = taskSync.GetById(taskId);
            if (task is null) return;

            var device = new CollectDeviceInfo(
                task.RecorderName, task.RecorderSerial, task.Protocol,
                RootPath: task.SourceRoot);
            var source = _sources.GetFor(device.Protocol);

            var pendingFiles = fileSync.GetPendingByTaskId(taskId);

            var speedLastTime = DateTime.MinValue;
            long speedLastBytes = 0;
            var cursor = 0;

            while (!ct.IsCancellationRequested && cursor < pendingFiles.Count)
            {
                var file = pendingFiles[cursor++];
                control.CurrentFileId = file.Id;

                file.BeginCopy(DateTime.Now);
                fileSync.Update(file);
                await PublishAndClearAsync(file);

                var baseCompletedBytes = task.CollectedBytes;
                var destination = Path.Combine(_options.CacheDirectory, task.TaskNo, file.RelativePath!);

                await source.CopyAsync(
                    device,
                    new SourceFileInfo(file.RelativePath, file.FileName, file.Size,
                        file.OriginalModifiedAt ?? DateTime.UtcNow),
                    destination,
                    progress =>
                    {
                        try
                        {
                            // 1. 聚合内 10% 节流：返回 true 表示跨档，需要更新字段
                            var crossed = file.ReportCopyProgress(progress);

                            // 2. 速度（跨文件连续采样）
                            var totalDoneBytes = baseCompletedBytes + (long)(file.Size * progress);
                            var nowUtc = DateTime.UtcNow;
                            if (speedLastTime != DateTime.MinValue)
                            {
                                var dt = (nowUtc - speedLastTime).TotalSeconds;
                                if (dt > 0.001)
                                {
                                    var speed = (totalDoneBytes - speedLastBytes) / dt;
                                    file.SetSpeed(speed);
                                    task.SetSpeed(speed);
                                }
                            }
                            speedLastTime = nowUtc;
                            speedLastBytes = totalDoneBytes;

                            // 3. UI 进度（同步写缓存，由 UI 层 150ms 合并）
                            var overall = task.TotalBytes > 0
                                ? Math.Clamp((double)totalDoneBytes / task.TotalBytes, 0, 1)
                                : 0;

                            _ui.PublishTaskProgress(taskId, overall * 100, task.SpeedBytesPerSecond);

                            // 4. 进度落库（按 10% 档）
                            if (crossed)
                            {
                                try { fileSync.Update(file); }
                                catch { /* 忽略进度落库失败 */ }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "进度回调异常 file={File}", file.FileName);
                        }
                        return Task.CompletedTask;
                    },
                    ct);

                // 校验大小
                file.BeginVerify();
                fileSync.Update(file);

                var copied = new FileInfo(destination).Length;
                if (copied != file.Size)
                    throw new IOException($"文件大小校验失败：期望 {file.Size}，实际 {copied}");

                // 摘要 + 签名 + 加密
                string? digest = null, digestAlgo = null, signature = null, signAlgo = null;
                var encryptedSize = 0L;
                var localEncrypted = false;

                try
                {
                    (digest, digestAlgo) = await _fileCrypto.ComputeDigestAsync(destination, ct);

                    if (_storageConfig.EnableFileSignature)
                    {
                        try
                        {
                            var meta = new FileMetadata(
                                FileNo: $"-{file.Id}",
                                FileName: file.FileName,
                                Size: file.Size,
                                ContentDigest: digest,
                                DigestAlgorithm: digestAlgo,
                                CollectedAt: DateTime.UtcNow,
                                StationCode: "");
                            (signature, signAlgo) = await _fileCrypto.SignMetadataAsync(meta, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "文件 {File} 元数据签名失败", file.FileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "文件 {File} 摘要计算失败", file.FileName);
                    file.FailCollecting($"摘要失败：{ex.Message}", DateTime.Now);
                    fileSync.Update(file);
                    await PublishAndClearAsync(file);
                    continue;
                }

                if (_options.EncryptCache)
                {
                    try
                    {
                        encryptedSize = await _fileCrypto.EncryptInPlaceAsync(destination, ct);
                        localEncrypted = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "文件 {File} 加密失败", file.FileName);
                        file.FailCollecting($"加密失败：{ex.Message}", DateTime.Now);
                        fileSync.Update(file);
                        await PublishAndClearAsync(file);
                        throw;
                    }
                }

                // 采集完成
                file.CompleteCollecting(digest, digestAlgo, signature, signAlgo,
                    localEncrypted, encryptedSize, DateTime.Now);
                fileSync.Update(file);
                await PublishAndClearAsync(file);
                _ui.PublishTaskFileCompleted(taskId, file.Id, file.Status);

                // 任务累计（长连接上直接更新）
                task.AddCollectedFile(file.Size, DateTime.Now);
                // taskSync.Update(task); // 不写 DB：进度字段由 ProgressAggregator 累加
                await PublishAndClearAsync(task);

                control.CurrentFileId = 0;
            }
        }
        catch (OperationCanceledException) { /* 正常取消 */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "采集任务 {TaskId} 执行异常（记录仪 {Recorder}）",
                taskId, task?.RecorderName);

            if (task is not null)
            {
                task.AddFailedFile(DateTime.Now);
                task.AppendErrorMessage(ex.Message);

                if (control.CurrentFileId != 0)
                {
                    var failedFile = fileSync.GetById(control.CurrentFileId);
                    if (failedFile is not null)
                    {
                        failedFile.FailCollecting(ex.Message, DateTime.Now);
                        fileSync.Update(failedFile);
                        await PublishAndClearAsync(failedFile);
                        _ui.PublishTaskFileCompleted(taskId, failedFile.Id, failedFile.Status);
                    }
                }

                taskSync.UpdateStateOnly(task);
                await PublishAndClearAsync(task);
            }
        }

        try
        {
            await FinalizeAsync(taskId, control);
        }
        catch (Exception ex)
        {
            _controls.TryRemove(taskId, out _);
            try
            {
                var shared = await _tasks.GetByIdAsync(taskId);
                if (shared is not null)
                {
                    shared.Fail($"收尾异常：{ex.Message}", DateTime.Now);
                    await _tasks.UpdateAsync(shared);
                    await PublishAndClearAsync(shared);

                    _logger.LogError(ex, "采集任务 {TaskId} 收尾异常", taskId);
                }
            }
            catch { /* 忽略二次失败 */ }
        }
    }

    private async Task FinalizeAsync(long taskId, TaskControl control)
    {
        using var scope = _scopeFactory.CreateScope();
        var taskSync = scope.ServiceProvider.GetRequiredService<ICollectTaskSyncRepository>();
        var fileSync = scope.ServiceProvider.GetRequiredService<ICollectFileSyncRepository>();

        var task = taskSync.GetById(taskId);
        if (task is null)
        {
            _controls.TryRemove(taskId, out _);
            return;
        }

        var files = fileSync.GetByTaskId(taskId).ToList();
        var current = control.CurrentFileId;

        // ---- 暂停分支 ----
        if (control.PauseRequested &&
            files.Any(f => f.Status is CollectFileStatus.Pending
                        or CollectFileStatus.Copying
                        or CollectFileStatus.Verifying))
        {
            if (current != 0)
            {
                var currentFile = files.FirstOrDefault(f => f.Id == current);
                if (currentFile is { Status: CollectFileStatus.Copying or CollectFileStatus.Verifying })
                {
                    currentFile.ResetToPending();
                    fileSync.Update(currentFile);
                    await PublishAndClearAsync(currentFile);
                    _ui.PublishTaskFileCompleted(taskId, currentFile.Id, currentFile.Status);
                }
            }

            task.Pause(DateTime.Now);
            //taskSync.Update(task);
            taskSync.UpdateStateOnly(task);
            await PublishAndClearAsync(task);

            _controls.TryRemove(taskId, out _);
            _logger.LogInformation("采集任务 {TaskId} 已暂停", taskId);
            return;
        }

        // ---- 终态分支 ----
        var interrupted = control.FinalStatus == CollectTaskStatus.Interrupted;
        var hasUnfinished = files.Any(f =>
            f.Status is CollectFileStatus.Pending
                or CollectFileStatus.Copying
                or CollectFileStatus.Verifying);

        var toUpdate = new List<CollectFile>();
        foreach (var file in files.Where(f =>
            f.Status is CollectFileStatus.Pending
                or CollectFileStatus.Copying
                or CollectFileStatus.Verifying))
        {
            if (interrupted) file.Interrupt("设备断开，采集中断", DateTime.Now);
            else file.Cancel("任务已取消", DateTime.Now);
            toUpdate.Add(file);
        }

        if (toUpdate.Count > 0)
        {
            fileSync.UpdateRange(toUpdate);
            foreach (var f in toUpdate)
            {
                await PublishAndClearAsync(f);
                _ui.PublishTaskFileCompleted(taskId, f.Id, f.Status);
            }
        }

        // 状态判定
        var terminalByRequest = control.FinalStatus is CollectTaskStatus.Interrupted
                                                   or CollectTaskStatus.Canceled;

        if (terminalByRequest && hasUnfinished)
        {
            if (interrupted) task.Interrupt("设备断开", DateTime.Now);
            else task.Cancel("任务已取消", DateTime.Now);
        }
        else
        {
            // 已全部完成（或正常收尾）
            task.Complete(DateTime.Now);

            if (control.FinalStatus != CollectTaskStatus.Canceled)
            {
                var completed = files.Where(f => f.Status == CollectFileStatus.Completed);
                TryErase(task, completed);
            }
        }

        taskSync.Update(task);
        await PublishAndClearAsync(task);
        _ui.PublishTaskStatus(taskId, task.Status, task.IsEmergency);

        _controls.TryRemove(taskId, out _);

        _logger.LogInformation(
            "采集任务 {TaskNo} 结束：{Status}（文件 {Total}/{Collected}）",
            task.TaskNo, task.Status, task.TotalFiles, task.CollectedFiles);
    }

    private void TryErase(CollectTask task, IEnumerable<CollectFile> files)
    {
        if (!_options.EraseAfterComplete) return;
        var list = files.ToList();
        if (list.Count == 0) return;

        var sourceFiles = list.Select(f => new SourceFileInfo(
            f.RelativePath, f.FileName, f.Size,
            f.OriginalModifiedAt ?? DateTime.Now));

        try
        {
            var device = new CollectDeviceInfo(
                task.RecorderName, task.RecorderSerial, task.Protocol,
                RootPath: task.SourceRoot);
            _sources.GetFor(device.Protocol)
                .EraseAsync(device, sourceFiles, CancellationToken.None)
                .GetAwaiter().GetResult();

            task.AppendErrorMessage("已擦除记录仪已采集文件");
        }
        catch (Exception ex)
        {
            task.AppendErrorMessage($"擦除失败：{ex.Message}");
        }
    }

    // =========================================================
    // 内部：事件发布
    // =========================================================

    private async Task PublishFilesAsync(IEnumerable<CollectFile> files, CancellationToken ct = default)
    {
        var all = new List<IDomainEvent>();
        foreach (var f in files)
        {
            if (f.DomainEvents.Count > 0)
            {
                all.AddRange(f.DomainEvents);
                f.ClearDomainEvents();
            }
        }
        if (all.Count > 0)
            await _eventBus.PublishRangeAsync(all, ct);
    }

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

    private static CollectTaskDto ToDto(CollectTask t) => new(
        t.Id, t.TaskNo, t.RecorderName, t.RecorderSerial, t.Protocol,
        t.OperatorUserId, t.DeptId, t.Status, t.IsAuto,
        t.TotalFiles, t.CollectedFiles, t.SkippedFiles, t.FailedFiles,
        t.TotalBytes, t.CollectedBytes, t.SpeedBytesPerSecond, t.IsEmergency,
        t.SyncStatus, t.UploadedFiles, t.UploadedBytes, t.UploadError,
        t.StartedAt, t.CompletedAt, t.ErrorMessage);

    private static CollectFileDto ToDto(CollectFile f) => new(
        f.Id, f.TaskId, f.FileName, f.Extension, f.Size,
        f.Status, f.Progress, f.SpeedBytesPerSecond,
        f.ErrorMessage, f.CollectedAt, f.RemotePath, f.UploadError);
}
