using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.IdGenerators;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Application.UsbPortCard.Events;
using Station.Domain.Collecting;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;
using Station.Domain.Security;

namespace Station.Application.Collecting;

/// <summary>
/// 采集任务服务
/// </summary>
public sealed class CollectTaskService : ICollectTaskService
{
    /// <summary>
    /// 采集任务控件
    /// </summary>
    private sealed class TaskControl
    {
        public CancellationTokenSource Cts { get; } = new();
        public volatile bool PauseRequested;
        public volatile CollectTaskStatus FinalStatus = CollectTaskStatus.Completed;
        public long CurrentFileId;
    }

    private readonly IRepository<CollectTask> _tasks;
    private readonly IRepository<CollectFile> _files;
    private readonly IIdGenerator _idGenerator;
    private readonly ICollectSourceProvider _sources;
    private readonly CollectOptions _options;
    private readonly ICollectControl _collectControl;
    private readonly ILicenseService _licenseService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<long, TaskControl> _controls = new();
    private readonly ILogger<CollectTaskService> _logger;
    private readonly IFileEncryptionService _encryptionService;
    private readonly IUsbPortCardEventService _eventService;

    public CollectTaskService(
        IRepository<CollectTask> tasks,
        IRepository<CollectFile> files,
        IIdGenerator idGenerator,
        ICollectSourceProvider sources,
        CollectOptions options,
        ICollectControl collectControl,
        ILicenseService licenseService,
        IServiceScopeFactory scopeFactory,
        IFileEncryptionService encryptionService,
        IUsbPortCardEventService eventService,
        ILogger<CollectTaskService> logger)
    {
        _tasks = tasks;
        _files = files;
        _idGenerator = idGenerator;
        _sources = sources;
        _options = options;
        _collectControl = collectControl;
        _licenseService = licenseService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _encryptionService = encryptionService;
        _eventService = eventService;
    }

    public async Task<CollectTaskDto> CreateTaskAsync(CollectDeviceInfo device, bool isAuto)
    {
        var now = DateTime.Now;
        var id = _idGenerator.NextId();
        var task = new CollectTask
        {
            Id = id,
            TaskNo = $"{device.Serial}-{now:yyyyMMddHHmmss}-{id % 1000000:D6}",
            RecorderName = device.Name,
            RecorderSerial = device.Serial,
            Protocol = device.Protocol,
            SourceRoot = device.RootPath,
            OperatorUserId = device.UserId,
            DeptId = device.DeptId,
            Status = CollectTaskStatus.Created,
            IsAuto = isAuto,
            CreatedAt = now
        };
        await _tasks.InsertAsync(task);
        _logger.LogInformation("采集任务创建 {TaskNo}：记录仪 {Recorder}（{Protocol}，{Mode}）",
            task.TaskNo, device.Name, device.Protocol, isAuto ? "自动" : "手动");

        if (isAuto && _options.AutoCollectOnConnect)
        {
            return await StartAsync(task.Id);
        }

        return ToDto(task);
    }

    public async Task<CollectTaskDto> StartAsync(long taskId)
    {
        if (!await _licenseService.IsValidNowAsync())
        {
            throw new InvalidOperationException("授权不可用，无法采集");
        }

        if (!_collectControl.CollectingEnabled)
        {
            throw new InvalidOperationException($"采集已停止：{_collectControl.StoppedReason ?? "远程指令"}");
        }

        var task = await RequireTaskAsync(taskId);
        if (_controls.ContainsKey(taskId))
        {
            throw new InvalidOperationException("任务正在运行中");
        }

        // ==================== 1. 扫描阶段 ====================
        task.Status = CollectTaskStatus.Scanning;
        task.StartedAt ??= DateTime.Now;
        task.ErrorMessage = null;
        await _tasks.UpdateAsync(task);
        _eventService.PublishTaskStatus(taskId, task.Status, false);

        var device = new CollectDeviceInfo(
            task.RecorderName, task.RecorderSerial, task.Protocol,
            RootPath: task.SourceRoot);
        var collectSource = _sources.GetFor(device.Protocol);
        var sources = await collectSource.ScanAsync(device, CancellationToken.None);

        var accepted = sources
            .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ==================== 2. 一次性查询已完成的指纹（合并 N+1 查询） ====================
        HashSet<string> completedFingerprints;
        if (_options.SkipCollected && accepted.Count > 0)
        {
            var fingerprints = accepted
                .Select(f => f.Fingerprint)
                .Where(fp => !string.IsNullOrEmpty(fp))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // 一次 SELECT 拉全表，内存过滤
            var completedList = await _files.GetListAsync(f =>
                fingerprints.Contains(f.Fingerprint) &&
                f.Status == CollectFileStatus.Completed);

            completedFingerprints = new HashSet<string>(
                completedList.Select(f => f.Fingerprint),
                StringComparer.Ordinal);
        }
        else
        {
            completedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        }

        // ==================== 3. 批量构造 CollectFile 列表（合并 N+1 插入） ====================
        var toInsert = new List<CollectFile>(accepted.Count);
        int skipped = 0;
        foreach (var source in accepted)
        {
            var already = _options.SkipCollected && completedFingerprints.Contains(source.Fingerprint);
            if (already) skipped++;

            toInsert.Add(new CollectFile
            {
                Id = _idGenerator.NextId(),
                TaskId = taskId,
                FileName = source.FileName,
                Extension = Path.GetExtension(source.FileName).ToLowerInvariant(),
                RelativePath = source.RelativePath,
                Size = source.Size,
                Fingerprint = source.Fingerprint,
                Status = already ? CollectFileStatus.Skipped : CollectFileStatus.Pending,
                OriginalModifiedAt = source.ModifiedAt
            });
        }

        if (toInsert.Count > 0)
        {
            await _files.InsertRangeAsync(toInsert);
        }

        _logger.LogInformation(
            "设备 {Recorder} 任务 {TaskNo} 扫描完成：{Total} 个文件（跳过 {Skipped}）",
            task.RecorderSerial, task.TaskNo, accepted.Count, skipped);

        task.TotalFiles = accepted.Count;
        task.SkippedFiles = skipped;
        task.TotalBytes = accepted.Sum(f => f.Size);
        task.Status = CollectTaskStatus.Collecting;
        await _tasks.UpdateAsync(task);
        _eventService.PublishTaskStatus(taskId, task.Status, false);

        // ==================== 4. 后台启动采集（LongRunning 专用线程，避免线程池饥饿） ====================
        var control = new TaskControl();
        _controls[taskId] = control;

        _ = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    RunCollectAsync(taskId, control).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "采集线程未捕获异常 {TaskId}", taskId);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return ToDto(task);
    }

    public Task PauseAsync(long taskId)
    {
        if (_controls.TryGetValue(taskId, out var control))
        {
            control.PauseRequested = true;
            control.Cts.Cancel();
            _logger.LogInformation("采集任务 {TaskId} 暂停请求", taskId);
        }

        return Task.CompletedTask;
    }

    public async Task ResumeAsync(long taskId)
    {
        var task = await RequireTaskAsync(taskId);
        if (_controls.ContainsKey(taskId))
        {
            throw new InvalidOperationException("任务正在运行中");
        }

        if (task.Status != CollectTaskStatus.Paused)
        {
            throw new InvalidOperationException("仅暂停中的任务可恢复");
        }

        task.Status = CollectTaskStatus.Collecting;
        task.ErrorMessage = null;
        await _tasks.UpdateAsync(task);

        var control = new TaskControl();
        _controls[taskId] = control;

        _ = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    RunCollectAsync(taskId, control).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "采集线程未捕获异常 {TaskId}", taskId);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        _logger.LogInformation("采集任务 {TaskId} 恢复", taskId);
    }

    public Task CancelAsync(long taskId) =>
        SetFinalAsync(taskId, CollectTaskStatus.Canceled);

    public Task InterruptAsync(long taskId, string reason) =>
        SetFinalAsync(taskId, CollectTaskStatus.Interrupted, reason);

    public async Task<bool> SetEmergencyAsync(long taskId, bool isEmergency)
    {
        var task = await RequireTaskAsync(taskId);
        if (isEmergency && !task.IsEmergency)
        {
            var current = await _tasks.CountAsync(t =>
                t.IsEmergency &&
                (t.Status == CollectTaskStatus.Scanning ||
                 t.Status == CollectTaskStatus.Collecting ||
                 t.Status == CollectTaskStatus.Paused));
            if (current >= _options.MaxEmergencyTasks)
            {
                return false;
            }
        }

        task.IsEmergency = isEmergency;
        await _tasks.UpdateAsync(task);
        _logger.LogInformation("任务 {TaskId} 紧急优先标记 -> {Value}", taskId, isEmergency);
        return true;
    }

    public async Task<CollectTaskDto?> GetTaskAsync(long taskId)
    {
        var task = await _tasks.GetByIdAsync(taskId);
        return task is null ? null : ToDto(task);
    }

    public async Task<IReadOnlyList<CollectTaskDto>> GetTasksAsync(int count)
    {
        var page = await _tasks.ToPageAsync(1, count, orderBy: t => t.CreatedAt, orderType: SqlSugar.OrderByType.Desc);
        return page.Items.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<CollectTaskDto>> GetActiveTasksAsync()
    {
        var list = await _tasks.GetListAsync(t =>
            t.Status == CollectTaskStatus.Scanning ||
            t.Status == CollectTaskStatus.Collecting ||
            t.Status == CollectTaskStatus.Paused);
        return list.OrderByDescending(t => t.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId)
    {
        var list = await _files.GetListAsync(f => f.TaskId == taskId);
        return list.OrderBy(f => f.Id).Select(ToDto).ToList();
    }

    // ---------- 内部 ----------

    private async Task SetFinalAsync(long taskId, CollectTaskStatus finalStatus, string? reason = null)
    {
        if (_controls.TryGetValue(taskId, out var control))
        {
            control.FinalStatus = finalStatus;
            if (!string.IsNullOrEmpty(reason))
            {
                var task = await _tasks.GetByIdAsync(taskId);
                if (task is not null)
                {
                    task.ErrorMessage = reason;
                    await _tasks.UpdateAsync(task);
                }
            }

            control.Cts.Cancel();
        }
        else if (await _tasks.GetByIdAsync(taskId) is { } idle)
        {
            idle.Status = finalStatus;
            idle.CompletedAt = DateTime.Now;
            idle.ErrorMessage = reason;
            await _tasks.UpdateAsync(idle);
            var pending = (await _files.GetListAsync(f => f.TaskId == taskId && f.Status == CollectFileStatus.Pending))
                .Select(f => { f.Status = CollectFileStatus.Canceled; return f; })
                .ToList();
            if (pending.Count > 0)
            {
                await _files.UpdateRangeAsync(pending);
            }
        }
    }

    /// <summary>
    /// 开始文件采集
    /// </summary>
    private async Task RunCollectAsync(long taskId, TaskControl control)
    {
        // 后台采集使用独立长连接客户端，DB 操作用同步调用（Kdbndp 异步长连接易报 command already in progress）
        using var scope = _scopeFactory.CreateScope();
        var taskLoop = scope.ServiceProvider.GetRequiredService<ILoopRepository<CollectTask>>();
        var fileLoop = scope.ServiceProvider.GetRequiredService<ILoopRepository<CollectFile>>();

        CollectTask? task = null;
        try
        {
            var ct = control.Cts.Token;
            task = taskLoop.GetById(taskId);
            if (task is null)
            {
                return;
            }

            var device = new CollectDeviceInfo(
                task.RecorderName, task.RecorderSerial, task.Protocol,
                RootPath: task.SourceRoot);
            var source = _sources.GetFor(device.Protocol);

            // ==================== 一次性拉取所有 Pending 文件（避免每次循环查库） ====================
            // 语义与原先"每轮查一条 Pending"等价：按 Id 升序依次处理；
            // 暂停/恢复时，FinalizeAsync 会把 Copying/Verifying 回退为 Pending，任务重启会重新拉取。
            var pendingFiles = fileLoop
                .GetList(f => f.TaskId == taskId && f.Status == CollectFileStatus.Pending)
                .OrderBy(f => f.Id)
                .ToList();

            // 速度计算：以任务累积字节作为采样点，跨文件连续；使用字段替代 Queue+LINQ
            var speedLastTime = DateTime.MinValue;
            long speedLastBytes = 0;

            // 授权检查节流：首次立即检查，之后每 5 秒一次
            var nextLicenseCheck = DateTime.UtcNow;

            var cursor = 0;
            while (!ct.IsCancellationRequested && cursor < pendingFiles.Count)
            {
                // 授权检查（节流）
                if (DateTime.UtcNow >= nextLicenseCheck)
                {
                    if (!await _licenseService.IsValidNowAsync())
                    {
                        // 授权到期：正在进行的采集立即中断
                        control.FinalStatus = CollectTaskStatus.Interrupted;
                        break;
                    }
                    nextLicenseCheck = DateTime.UtcNow.AddSeconds(5);
                }

                var file = pendingFiles[cursor++];

                // 记录当前文件的起始状态
                control.CurrentFileId = file.Id;
                file.Status = CollectFileStatus.Copying;
                file.Progress = 0;
                fileLoop.Update(file);

                // 任务已完成的旧文件总字节数
                var baseCompletedBytes = task.CollectedBytes;
                var destination = Path.Combine(_options.CacheDirectory, task.TaskNo, file.RelativePath);
                double lastPersistedProgress = -1;

                // 开始复制，传入进度回调（无 async，返回 Task.CompletedTask 消除状态机分配）
                await source.CopyAsync(
                    device,
                    new SourceFileInfo(
                        file.RelativePath, file.FileName, file.Size,
                        file.OriginalModifiedAt ?? DateTime.UtcNow),
                    destination,
                    progress =>
                    {
                        // 1. 更新单个文件进度
                        file.Progress = progress;

                        // 2. 任务级累积字节（用 totalDoneBytes 作为速度样本，跨文件连续）
                        var totalDoneBytes = baseCompletedBytes + (long)(file.Size * progress);
                        double overallProgress = task.TotalBytes > 0
                            ? Math.Clamp((double)totalDoneBytes / task.TotalBytes, 0, 1)
                            : 0;

                        // 3. 速度计算：两次采样差值（无 LINQ / Queue 分配）
                        var now = DateTime.UtcNow;
                        if (speedLastTime != DateTime.MinValue)
                        {
                            var dt = (now - speedLastTime).TotalSeconds;
                            if (dt > 0.001)
                            {
                                var speed = (totalDoneBytes - speedLastBytes) / dt;
                                file.SpeedBytesPerSecond = speed;
                                task.SpeedBytesPerSecond = speed;
                            }
                        }
                        speedLastTime = now;
                        speedLastBytes = totalDoneBytes;

                        // 4. 发布进度事件（同步写入缓存，500ms 合并派发，无阻塞）
                        _eventService.PublishTaskProgress(
                            taskId,
                            overallProgress * 100,
                            task.SpeedBytesPerSecond);

                        // 5. 进度落库节流（≥10% 增量），失败不中断复制
                        if (progress - lastPersistedProgress >= 0.1)
                        {
                            lastPersistedProgress = progress;
                            try
                            {
                                fileLoop.Update(file);
                            }
                            catch
                            {
                                // 忽略进度落库失败
                            }
                        }

                        return Task.CompletedTask;
                    },
                    ct);

                // ==================== 校验大小 ====================
                file.Status = CollectFileStatus.Verifying;
                fileLoop.Update(file);

                var copied = new FileInfo(destination).Length;
                if (copied != file.Size)
                {
                    throw new IOException($"文件大小校验失败：期望 {file.Size}，实际 {copied}");
                }

                if (_options.EncryptCache)
                {
                    _encryptionService.EncryptInPlace(destination);
                }

                file.Status = CollectFileStatus.Completed;
                file.Progress = 1;
                file.CollectedAt = DateTime.Now;
                fileLoop.Update(file);

                task.CollectedFiles++;
                task.CollectedBytes += file.Size;
                task.Status = CollectTaskStatus.Collecting;
                taskLoop.Update(task);
                _eventService.PublishTaskStatus(taskId, task.Status, false);

                control.CurrentFileId = 0;
            }
        }
        catch (OperationCanceledException)
        {
            // 中断/取消/暂停
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "采集任务 {TaskId} 执行异常（记录仪 {Recorder}）", taskId, task?.RecorderName);
            if (task is not null)
            {
                task.FailedFiles++;
                task.ErrorMessage = ex.Message;
                var failedFile = fileLoop.GetById(control.CurrentFileId);
                if (failedFile is not null)
                {
                    failedFile.Status = CollectFileStatus.Failed;
                    failedFile.ErrorMessage = ex.Message;
                    fileLoop.Update(failedFile);
                }

                taskLoop.Update(task);
            }
        }

        try
        {
            FinalizeAsync(taskId, control, taskLoop, fileLoop);
        }
        catch (Exception ex)
        {
            // 兜底：收尾失败不产生未观察异常；尝试用共享仓储将任务标记为失败并记录原因
            _controls.TryRemove(taskId, out _);
            try
            {
                var shared = await _tasks.GetByIdAsync(taskId);
                if (shared is not null)
                {
                    shared.Status = CollectTaskStatus.Failed;
                    shared.CompletedAt = DateTime.Now;
                    shared.ErrorMessage = $"收尾异常：{ex.Message}";
                    await _tasks.UpdateAsync(shared);
                    _eventService.PublishTaskStatus(taskId, shared.Status, false);
                    _logger.LogError(ex, "采集任务 {TaskId} 收尾异常（记录仪 {Recorder}）", taskId, task?.RecorderName);
                }
            }
            catch
            {
                // 忽略二次失败
            }
        }
    }

    private void FinalizeAsync(long taskId, TaskControl control, ILoopRepository<CollectTask> taskLoop,
    ILoopRepository<CollectFile> fileLoop)
    {
        var task = taskLoop.GetById(taskId);
        if (task is null)
        {
            _controls.TryRemove(taskId, out _);
            return;
        }

        var files = fileLoop.GetList(f => f.TaskId == taskId).ToList();
        var current = control.CurrentFileId;

        if (control.PauseRequested &&
            files.Any(f => f.Status is CollectFileStatus.Pending or CollectFileStatus.Copying or CollectFileStatus.Verifying))
        {
            // 暂停：当前文件回到待采集，其余保持待采集
            if (current != 0)
            {
                var currentFile = files.FirstOrDefault(f => f.Id == current);
                if (currentFile is { Status: CollectFileStatus.Copying or CollectFileStatus.Verifying })
                {
                    currentFile.Status = CollectFileStatus.Pending;
                    currentFile.Progress = 0;
                    fileLoop.Update(currentFile);
                }
            }

            task.Status = CollectTaskStatus.Paused;
            taskLoop.Update(task);
            _controls.TryRemove(taskId, out _);
            _eventService.PublishTaskStatus(taskId, task.Status, false); // 发布采集任务状态事件
            _logger.LogInformation("采集任务 {TaskId} 已暂停", taskId);
            return;
        }

        // 中断/取消：当前文件异常，未开始文件取消
        var interrupted = control.FinalStatus == CollectTaskStatus.Interrupted;
        var hasUnfinished = files.Any(f =>
            f.Status is CollectFileStatus.Pending or CollectFileStatus.Copying or CollectFileStatus.Verifying);
        foreach (var file in files.Where(f =>
                     f.Status is CollectFileStatus.Pending or CollectFileStatus.Copying or CollectFileStatus.Verifying))
        {
            file.Status = interrupted ? CollectFileStatus.Abnormal : CollectFileStatus.Canceled;
            file.ErrorMessage = interrupted ? "设备断开，采集中断" : "任务已取消";
            fileLoop.Update(file);
        }

        var terminalByRequest = control.FinalStatus is CollectTaskStatus.Interrupted or CollectTaskStatus.Canceled;
        // 到期/取消时若文件已全部完成，保持 Completed，避免误报中断
        task.Status = terminalByRequest && hasUnfinished ? control.FinalStatus : CollectTaskStatus.Completed;
        task.CompletedAt = DateTime.Now;
        if (task.Status == CollectTaskStatus.Completed && control.FinalStatus != CollectTaskStatus.Canceled)
        {
            var sourceFiles = files.Where(f => f.Status == CollectFileStatus.Completed)
                    ;
            TryErase(task, sourceFiles);
        }

        taskLoop.Update(task);
        _controls.TryRemove(taskId, out _);
        _eventService.PublishTaskStatus(taskId, task.Status, false); // 发布采集任务状态事件
        _logger.LogInformation("采集任务 {TaskId} 结束：{Status}（文件 {Total}/{Collected}）",
            taskId, task.Status, task.TotalFiles, task.CollectedFiles);

    }

    private void TryErase(CollectTask task, IEnumerable<CollectFile> files)
    {
        if (!_options.EraseAfterComplete)
        {
            return;
        }
        if (files.Count() <= 0)
        {
            return;
        }

        var sourceFiles = files.Select(f => new SourceFileInfo(f.RelativePath, f.FileName, f.Size, f.OriginalModifiedAt ?? DateTime.Now));

        try
        {
            var device = new CollectDeviceInfo(
                task.RecorderName, task.RecorderSerial, task.Protocol,
                RootPath: task.SourceRoot);
            _sources.GetFor(device.Protocol)
                .EraseAsync(device, sourceFiles, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            task.ErrorMessage = task.ErrorMessage is null ? "已擦除记录仪已采集文件" : task.ErrorMessage + "；已擦除记录仪已采集文件";
        }
        catch (Exception ex)
        {
            // 擦除失败：重试交给 P1 报警，不阻塞任务
            task.ErrorMessage = task.ErrorMessage is null ? $"擦除失败：{ex.Message}" : task.ErrorMessage + $"；擦除失败：{ex.Message}";
        }
    }

    private async Task<CollectTask> RequireTaskAsync(long taskId) =>
        await _tasks.GetByIdAsync(taskId) ?? throw new InvalidOperationException($"任务 {taskId} 不存在");

    private static CollectTaskDto ToDto(CollectTask t) => new(
        t.Id, t.TaskNo, t.RecorderName, t.RecorderSerial, t.Protocol, t.OperatorUserId, t.DeptId,
        t.Status, t.IsAuto,
        t.TotalFiles, t.CollectedFiles, t.SkippedFiles, t.FailedFiles,
        t.TotalBytes, t.CollectedBytes, t.SpeedBytesPerSecond, t.IsEmergency,
        t.SyncStatus, t.UploadedFiles, t.UploadedBytes, t.UploadError,
        t.StartedAt, t.CompletedAt, t.ErrorMessage);

    private static CollectFileDto ToDto(CollectFile f) => new(
        f.Id, f.TaskId, f.FileName, f.Extension, f.Size,
        f.Status, f.Progress, f.SpeedBytesPerSecond, f.ErrorMessage, f.CollectedAt, f.RemotePath, f.UploadError);
}
