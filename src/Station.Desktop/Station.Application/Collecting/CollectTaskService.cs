using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Station.Application.Collecting.Dtos;
using Station.Application.Licensing;
using Station.Application.Notifications;
using Station.Application.PlatformSync;
using Station.Application.Recorders;
using Station.Application.Security;
using Station.Application.Storages;
using Station.Crypto;
using Station.Data.IdGeneration;
using Station.Device.FileSystem;
using Station.Domain.Abstractions;
using Station.Domain.Collecting;

namespace Station.Application.Collecting;

/// <summary>采集任务应用服务（聚合仓储 + 领域事件 + UI 通知）。</summary>
public sealed class CollectTaskService : ICollectTaskService
{
    private sealed class TaskControl
    {
        public CancellationTokenSource Cts { get; } = new();
        public volatile bool PauseRequested;
        public volatile CollectTaskStatus FinalStatus = CollectTaskStatus.Completed;
        public long CurrentFileId;
    }

    private readonly ICollectTaskRepository _tasks;
    private readonly ICollectFileRepository _files;
    private readonly ICollectTaskQueryService _taskQuery;
    private readonly IIdGenerator _idGenerator;
    private readonly IDeviceFileSystemProvider _sources;
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
    private readonly ConcurrentDictionary<long, long> _uiVersions = new();

    private readonly ILogger<CollectTaskService> _logger;

    public CollectTaskService(
        ICollectTaskRepository tasks,
        ICollectFileRepository files,
        ICollectTaskQueryService taskQuery,
        IIdGenerator idGenerator,
        IDeviceFileSystemProvider sources,
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

    // ========== 创建 ==========

    /// <inheritdoc />
    public async Task<CollectTaskDto> CreateTaskAsync(
        BoundDevice device, bool isAuto, CancellationToken ct = default)
    {
        // 1. 生成主键 + 任务号
        var now = DateTime.Now;
        var id = _idGenerator.NewId();
        var taskNo = await _taskNoGen.NextAsync(
            _station.Current.StationCode, device.RecorderCode!, now, ct);

        // 2. 构造 DeviceRef（不可变设备快照）
        var deviceRef = DeviceRef.Create(
            key: device.Device.Key,
            physicalPortId: device.Device.PhysicalPortId,
            serial: device.Device.Serial,
            root: device.Device.Root,
            name: device.Device.Name,
            protocol: device.Device.Protocol);

        // 3. 创建聚合根
        var task = CollectTask.Create(
            id, taskNo,
            recorderId: device.RecorderId ?? 0,
            recorderCode: device.RecorderCode ?? string.Empty,
            device: deviceRef,
            userId: device.UserId,
            deptId: device.DeptId,
            isAuto: isAuto,
            now: now);

        // 4. 落库 + 发布领域事件 + 发 UI 状态事件
        await _tasks.AddAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        PublishTaskStatusUi(task);

        _logger.LogInformation(
            "采集任务创建 {TaskNo}：记录仪 {Recorder}（{Protocol}，{Mode}，Key={Key}，Port={Port}）",
            task.TaskNo, deviceRef.Name, deviceRef.Protocol,
            isAuto ? "自动" : "手动", deviceRef.Key, deviceRef.PhysicalPortId ?? "-");

        // 5. 自动采集
        if (isAuto && _options.AutoCollectOnConnect)
        {
            return await StartAsync(task.Id, ct);
        }

        return ToDto(task);
    }

    // ========== 启动 / 续采 ==========

    /// <inheritdoc />
    public async Task<CollectTaskDto> StartAsync(long taskId, CancellationToken ct = default)
    {
        // 1. 前置校验：授权 + 采集开关
        if (!await _licenseService.IsValidNowAsync())
            throw new InvalidOperationException("授权不可用，无法采集");

        if (!_collectControl.CollectingEnabled)
            throw new InvalidOperationException($"采集已停止：{_collectControl.StoppedReason ?? "远程指令"}");

        // 2. 加载任务 + 校验无活动控制块
        var task = await _tasks.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"任务 {taskId} 不存在");

        if (_controls.ContainsKey(taskId))
            throw new InvalidOperationException("任务正在运行中");

        // 3. 记录原始状态（区分"新建"与"续采"）
        var originalStatus = task.Status;

        // 4. 状态切换：新建 / 续采均走 BeginScanning
        if (originalStatus == CollectTaskStatus.Created
            || originalStatus is CollectTaskStatus.Interrupted
                                or CollectTaskStatus.Failed
                                or CollectTaskStatus.Paused)
        {
            task.BeginScanning(DateTime.Now);
        }
        else
        {
            throw new InvalidOperationException(
                $"任务状态 {originalStatus} 不允许启动/续采");
        }

        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        PublishTaskStatusUi(task);

        // 5. 从聚合根统一构造 DetectedDevice（TaskDeviceFactory）
        var device = TaskDeviceFactory.FromCollectTask(task);

        // 6. 扫描设备文件
        var collectSource = _sources.GetFor(device.Protocol);
        var sources = await collectSource.ScanAsync(device, extensions: _options.ScanExtensions, _options.ScanIncludePaths, _options.ScanExtensions, ct: ct);
        var accepted = sources.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();

        // 7. 查已采集指纹（按 fingerprint 跳过已完成文件）
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

        // 8. 批量构造 CollectFile（只对新文件）
        using var fileCtx = await _fileNoGen.CreateContextAsync(task.TaskNo, ct);
        var toInsert = new List<CollectFile>(accepted.Count);
        var skipped = 0;
        var newPendingCount = 0;
        long newPendingBytes = 0;

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

            if (!already)
            {
                newPendingCount++;
                newPendingBytes += source.Size;
            }
        }

        if (toInsert.Count > 0)
            await _files.AddRangeAsync(toInsert, ct);

        _logger.LogInformation(
            "设备 {Recorder} 任务 {TaskNo} 扫描完成：{Total} 个文件（跳过 {Skipped}，新增 {New}）",
            task.Device.Serial, task.TaskNo, accepted.Count, skipped, newPendingCount);

        // 9. 状态机：新建 vs 续采
        if (originalStatus == CollectTaskStatus.Created)
        {
            task.BeginCollecting(
                accepted.Count, skipped,
                accepted.Sum(f => f.Size), DateTime.Now);
        }
        else
        {
            // 续采：累加统计（保留历史 Completed）
            task.ResumeCollecting(newPendingCount, newPendingBytes, DateTime.Now);
        }

        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        PublishTaskStatusUi(task);

        // 10. 启动 LongRunning 后台采集线程
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

    // ========== 暂停 / 恢复 / 取消 / 中断 / 紧急 ==========

    /// <inheritdoc />
    public Task PauseAsync(long taskId, CancellationToken ct = default)
    {
        // 1. 有活动控制块：设置暂停信号 + 触发取消
        if (_controls.TryGetValue(taskId, out var control))
        {
            control.PauseRequested = true;
            control.Cts.Cancel();
            _logger.LogInformation("采集任务 {TaskId} 暂停请求", taskId);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ResumeAsync(long taskId, CancellationToken ct = default)
    {
        // 1. 加载任务 + 校验无运行中控制块
        var task = await _tasks.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"任务 {taskId} 不存在");

        if (_controls.ContainsKey(taskId))
            throw new InvalidOperationException("任务正在运行中");

        // 2. 状态切换 Paused → Collecting
        task.Resume(DateTime.Now);
        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        PublishTaskStatusUi(task);

        // 3. 重启后台采集线程
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

    /// <inheritdoc />
    public Task CancelAsync(long taskId, CancellationToken ct = default)
        => SetFinalAsync(taskId, CollectTaskStatus.Canceled, null, ct);

    /// <inheritdoc />
    public Task InterruptAsync(long taskId, string reason, CancellationToken ct = default)
        => SetFinalAsync(taskId, CollectTaskStatus.Interrupted, reason, ct);

    /// <inheritdoc />
    public async Task<bool> SetEmergencyAsync(
        long taskId, bool isEmergency, CancellationToken ct = default)
    {
        // 1. 加载任务
        var task = await _tasks.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"任务 {taskId} 不存在");

        // 2. 设为紧急时校验上限
        if (isEmergency && !task.IsEmergency)
        {
            var current = await _tasks.CountActiveEmergencyAsync(ct);
            if (current >= _options.MaxEmergencyTasks)
                return false;
        }

        // 3. 更新紧急标记 + 持久化 + 事件
        task.MarkEmergency(isEmergency, DateTime.Now);
        await _tasks.UpdateAsync(task, ct);
        await PublishAndClearAsync(task, ct);
        PublishTaskStatusUi(task);

        _logger.LogInformation("任务 {TaskId} 紧急优先标记 -> {Value}", taskId, isEmergency);
        return true;
    }

    // ========== 批量控制（优雅关闭） ==========

    /// <inheritdoc />
    public async Task PauseAllAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        // 1. 快照当前运行中的 taskId 列表
        var ids = _controls.Keys.ToList();
        if (ids.Count == 0) return;

        _logger.LogInformation("批量暂停：{Count} 个运行中任务", ids.Count);

        // 2. 逐个发送暂停信号（不阻塞等待）
        foreach (var id in ids)
        {
            try { await PauseAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "暂停 {TaskId} 失败", id); }
        }

        // 3. 轮询等待 _controls 清空（FinalizeAsync 完成后会移除控制块）
        var deadline = DateTime.UtcNow + timeout;
        while (_controls.Count > 0 && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(100, ct);
        }

        if (_controls.Count > 0)
            _logger.LogWarning("批量暂停超时：{Remain} 个任务未完成收尾", _controls.Count);
        else
            _logger.LogInformation("批量暂停完成");
    }

    // =========================================================
    // 读
    // =========================================================

    /// <inheritdoc />
    public Task<CollectTaskDto?> GetTaskAsync(long taskId, CancellationToken ct = default)
        => _taskQuery.GetByIdAsync(taskId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectTaskDto>> GetTasksAsync(
        int count, CancellationToken ct = default)
        => await _taskQuery.GetRecentAsync(count, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectTaskDto>> GetActiveTasksAsync(
        CancellationToken ct = default)
        => await _taskQuery.GetActiveAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(
        long taskId, CancellationToken ct = default)
    {
        var files = await _files.GetByTaskIdAsync(taskId, ct);
        return files.OrderBy(f => f.Id).Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<CollectTaskDto?> FindResumeableTaskAsync(
        string? physicalPortId, CancellationToken ct = default)
    {
        // 1. physicalPortId 为空直接返回（降级场景无法判定）
        if (string.IsNullOrEmpty(physicalPortId)) return null;

        // 2. 取最近 100 个任务（覆盖绝大多数场景）
        var recent = await _taskQuery.GetRecentAsync(100, ct);

        // 3. 过滤：同端口 + Paused / Interrupted / Failed
        var normalized = physicalPortId.Trim().ToUpperInvariant();
        return recent.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.PhysicalPortId)
            && string.Equals(t.PhysicalPortId.Trim().ToUpperInvariant(), normalized,
                StringComparison.OrdinalIgnoreCase)
            && t.Status is CollectTaskStatus.Paused
                        or CollectTaskStatus.Interrupted
                        or CollectTaskStatus.Failed);
    }

    /// <inheritdoc />
    public long GetUiVersion(long taskId)
        => _uiVersions.TryGetValue(taskId, out var v) ? v : 0;

    // =========================================================
    // 内部：收尾
    // =========================================================

    /// <summary>
    /// 统一终态处理入口（Cancel / Interrupt / Fail）。
    /// 
    /// 【有活动控制块】
    ///   - 设置 FinalStatus，触发 Cts.Cancel
    ///   - 由后台线程 FinalizeAsync 完成实际落库
    /// 
    /// 【无活动控制块】
    ///   - 直接对空闲任务执行终态
    ///   - 更新未完成文件状态（Interrupt / Cancel）
    /// </summary>
    private async Task SetFinalAsync(
        long taskId, CollectTaskStatus finalStatus, string? reason,
        CancellationToken ct = default)
    {
        // 1. 有活动控制块：设置信号后交给 FinalizeAsync
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
                    task.ClearDomainEvents();
                }
            }

            control.Cts.Cancel();
            return;
        }

        // 2. 无活动控制块：直接终态
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
        PublishTaskStatusUi(idle);

        // 3. 更新未完成文件状态
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
    // 内部：后台采集主循环
    // =========================================================

    /// <summary>
    /// 后台采集主线程（长连接）。
    /// 
    /// 【步骤】
    ///   1. 独立 Scope 获取长连接仓储
    ///   2. 统一构造 DetectedDevice
    ///   3. 逐个 Pending 文件：BeginCopy → CopyAsync → Verify → Digest → Sign → Encrypt → CompleteCollecting
    ///   4. 每完成一个文件：AddCollectedFile + 发 UI 事件
    ///   5. 收尾：FinalizeAsync
    /// </summary>
    private async Task RunCollectAsync(long taskId, TaskControl control)
    {
        // 1. 独立 Scope（长连接仓储）
        using var scope = _scopeFactory.CreateScope();
        var taskSync = scope.ServiceProvider.GetRequiredService<ICollectTaskSyncRepository>();
        var fileSync = scope.ServiceProvider.GetRequiredService<ICollectFileSyncRepository>();

        CollectTask? task = null;
        try
        {
            var ct = control.Cts.Token;

            // 2. 加载任务 + 构造 DetectedDevice
            task = taskSync.GetById(taskId);
            if (task is null) return;

            var device = TaskDeviceFactory.FromCollectTask(task);
            var portId = device.PhysicalPortId;

            var source = _sources.GetFor(device.Protocol);
            var pendingFiles = fileSync.GetPendingByTaskId(taskId);

            var speedLastTime = DateTime.MinValue;
            long speedLastBytes = 0;
            var cursor = 0;

            // 3. 逐文件处理
            while (!ct.IsCancellationRequested && cursor < pendingFiles.Count)
            {
                var file = pendingFiles[cursor++];
                control.CurrentFileId = file.Id;

                // 3.1 标记复制中
                file.BeginCopy(DateTime.Now);
                fileSync.Update(file);
                await PublishAndClearAsync(file);

                var baseCompletedBytes = task.CollectedBytes;
                var destination = Path.Combine(_options.CacheDirectory, task.TaskNo, file.RelativePath!);

                // 3.2 复制（带进度回调）
                await source.CopyAsync(
                    device,
                    new SourceFileInfo(file.RelativePath!, file.FileName, file.Size,
                        file.OriginalModifiedAt ?? DateTime.UtcNow),
                    destination,
                    progress =>
                    {
                        try
                        {
                            // 聚合内 10% 节流
                            var crossed = file.ReportCopyProgress(progress);

                            // 速度采样
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

                            // UI 进度事件
                            var overall = task.TotalBytes > 0
                                ? Math.Clamp((double)totalDoneBytes / task.TotalBytes, 0, 1)
                                : 0;

                            _ui.PublishTaskProgress(
                                taskId, overall * 100, task.SpeedBytesPerSecond,
                                portId, GetUiVersion(taskId));

                            // 跨档时落库
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

                // 3.3 校验大小
                file.BeginVerify();
                fileSync.Update(file);

                var copied = new FileInfo(destination).Length;
                if (copied != file.Size)
                    throw new IOException($"文件大小校验失败：期望 {file.Size}，实际 {copied}");

                // 3.4 摘要 + 签名 + 加密
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

                // 3.5 完成采集
                file.CompleteCollecting(digest, digestAlgo, signature, signAlgo,
                    localEncrypted, encryptedSize, DateTime.Now);
                fileSync.Update(file);
                await PublishAndClearAsync(file);
                _ui.PublishTaskFileCompleted(taskId, file.Id, file.Status);

                // 3.6 累加任务统计
                task.AddCollectedFile(file.Size, DateTime.Now);
                await PublishAndClearAsync(task);

                control.CurrentFileId = 0;
            }
        }
        catch (OperationCanceledException) { /* 正常取消 */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "采集任务 {TaskId} 执行异常（记录仪 {Recorder}）",
                taskId, task?.Device.Name);

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

        // 4. 收尾
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

    /// <summary>
    /// 收尾：处理暂停 / 终态分支。
    /// 
    /// 【暂停分支】
    ///   - 当前文件 ResetToPending
    ///   - task.Pause
    ///   - UpdateStateOnly
    /// 
    /// 【终态分支】
    ///   - 未完成文件 Interrupt / Cancel
    ///   - 任务 Interrupt / Cancel / Complete
    ///   - TryErase（若配置 EraseAfterComplete）
    /// </summary>
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
            taskSync.UpdateStateOnly(task);
            await PublishAndClearAsync(task);
            PublishTaskStatusUi(task);

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
            task.Complete(DateTime.Now);

            if (control.FinalStatus != CollectTaskStatus.Canceled)
            {
                var completed = files.Where(f => f.Status == CollectFileStatus.Completed);
                TryErase(task, completed);
            }
        }

        taskSync.Update(task);
        await PublishAndClearAsync(task);
        PublishTaskStatusUi(task);

        _controls.TryRemove(taskId, out _);

        _logger.LogInformation(
            "采集任务 {TaskNo} 结束：{Status}（文件 {Total}/{Collected}）",
            task.TaskNo, task.Status, task.TotalFiles, task.CollectedFiles);
    }

    /// <summary>尝试擦除记录仪上的已采集文件（EraseAfterComplete=true 时）。</summary>
    private void TryErase(CollectTask task, IEnumerable<CollectFile> files)
    {
        if (!_options.EraseAfterComplete) return;
        var list = files.ToList();
        if (list.Count == 0) return;

        var sourceFiles = list.Select(f => new SourceFileInfo(
            f.RelativePath!, f.FileName, f.Size,
            f.OriginalModifiedAt ?? DateTime.Now));

        try
        {
            var device = TaskDeviceFactory.FromCollectTask(task);

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

    private void PublishTaskStatusUi(CollectTask task)
    {
        _ui.PublishTaskStatus(
            task.Id, task.Status, task.IsEmergency,
            task.Device.PhysicalPortId, NextUiVersion(task.Id));
    }

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
       t.Id, t.TaskNo, t.Device.Key, t.RecorderId, t.RecorderCode,
       t.Device.Name, t.Device.Serial, t.Device.Protocol, t.Device.PhysicalPortId,
       t.OperatorUserId, "占位", "占位", t.DeptId, "占位", t.Status, t.IsAuto,
       t.TotalFiles, t.CollectedFiles, t.SkippedFiles, t.FailedFiles,
       t.TotalBytes, t.CollectedBytes, t.SpeedBytesPerSecond, t.IsEmergency,
       t.SyncStatus, t.UploadedFiles, t.UploadedBytes, t.UploadError,
       t.StartedAt, t.CompletedAt, t.ErrorMessage, t.CreatedAt);

    private static CollectFileDto ToDto(CollectFile f) => new(
        f.Id, f.TaskId, f.FileName, f.Extension, f.Size,
        f.Status, f.Progress, f.SpeedBytesPerSecond,
        f.ErrorMessage, f.CollectedAt, f.RemotePath, f.UploadError);

    private long NextUiVersion(long taskId)
        => _uiVersions.AddOrUpdate(taskId, 1, (_, v) => v + 1);
}
