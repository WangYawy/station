using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Station.Application.Collecting;
using Station.Application.Collecting.Dtos;
using Station.Application.Notifications;
using Station.Application.OperationAccess;
using Station.Application.Session;
using Station.Application.Settings;
using Station.Application.UsbPortCards;
using Station.Contracts;
using Station.Desktop.Views;
using Station.Device.Detection;
using Station.Device.Events;

namespace Station.Desktop.ViewModels;

/// <summary>
/// 30 路卡片工作台。
/// 变更点：
///   - 卡片按 SlotIndex 固定创建，不再"空槽匹配"
///   - 事件按 ISlotRouter 路由到固定槽位
///   - Fixed 模式下未映射端口显示 #??
///   - 版本号仲裁避免旧事件覆盖
///   - 设备断开时由卡片内部 3 秒延迟清空
/// </summary>
public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly ICollectTaskService _collectService;
    private readonly IOperationAccessService _operationAccess;
    private readonly IUsbPortCardService _cardService;
    private readonly IDevicePresenceService _presence;
    private readonly ISlotRouter _slotRouter;
    private readonly IUiNotificationService _eventService;
    private readonly WindowModeOptions _options;
    private readonly CollectOptions _collectOptions;
    private readonly ILogger<WorkbenchViewModel> _logger;

    private readonly DispatcherTimer _heartbeatTimer;
    private readonly DispatcherTimer _progressFlushTimer;
    private int _refreshing;

    private readonly ConcurrentDictionary<long, ProgressSnapshot> _progressBuffer = new();
    private readonly record struct ProgressSnapshot(double Percent, double SpeedBytesPerSecond);

    /// <summary>DeviceKey → Slot 缓存（事件路由加速）。</summary>
    private readonly ConcurrentDictionary<string, int> _deviceSlotCache = new(StringComparer.Ordinal);

    #region 卡片布局

    public ObservableCollection<UsbPortCardViewModel> PortCards { get; }

    [ObservableProperty] private int _columns = 6;
    [ObservableProperty] private int _rows = 5;
    [ObservableProperty] private bool _canOperate;
    [ObservableProperty] private double _cardWidth = 240;
    [ObservableProperty] private double _cardHeight = 80;
    [ObservableProperty] private double _gridHeight;

    public double MinCardWidth => _options.MinCardWidth;
    public double MinCardHeight => _options.MinCardHeight;

    #endregion

    public WorkbenchViewModel(
        ISessionManager sessions,
        ICollectTaskService collectService,
        IOperationAccessService operationAccess,
        IUsbPortCardService cardService,
        IDevicePresenceService presence,
        ISlotRouter slotRouter,
        IUiNotificationService eventService,
        WindowModeOptions options,
        CollectOptions collectOptions,
        ILogger<WorkbenchViewModel> logger)
    {
        _sessions = sessions;
        _collectService = collectService;
        _operationAccess = operationAccess;
        _cardService = cardService;
        _presence = presence;
        _slotRouter = slotRouter;
        _eventService = eventService;
        _options = options;
        _collectOptions = collectOptions;
        _logger = logger;

        Columns = Math.Clamp(options.Columns, 1, 10);
        Rows = Math.Clamp(options.Rows, 1, 10);
        CardWidth = options.MinCardWidth;
        CardHeight = options.MinCardHeight;

        // 事件订阅
        _eventService.TaskProgressUpdated += OnTaskProgressUpdated;
        _eventService.TaskStatusChanged += OnTaskStatusChanged;
        _eventService.DeviceConnected += OnDeviceConnected;
        _eventService.DeviceDisconnected += OnDeviceDisconnected;
        _eventService.DeviceBound += OnDeviceBound;
        _eventService.DeviceRejected += OnDeviceRejected;

        PortCards = [];
        BuildFixedLayout();

        _sessions.SessionChanged += OnSessionChanged;
        OnSessionChanged();

        // 心跳（5 秒快照兜底）
        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatTimer.Tick += async (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
            try { await RefreshSnapshotAsync(); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        };
        _heartbeatTimer.Start();

        // 进度缓冲（150ms 合并）
        _progressFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _progressFlushTimer.Tick += (_, _) => FlushProgressBuffer();
        _progressFlushTimer.Start();

        _ = RefreshSnapshotAsync();
    }

    #region 卡片布局

    /// <summary>按 SlotRouter.Capacity 固定生成卡片（30 张）。</summary>
    private void BuildFixedLayout()
    {
        var total = Math.Max(_slotRouter.Capacity, 1);
        for (var i = 0; i < total; i++)
            PortCards.Add(CreateCard(i, portNumber: i + 1));
    }

    private UsbPortCardViewModel CreateCard(int slotIndex, int portNumber)
        => new(
            slotIndex,
            portNumber,
            pause: c => _ = OperateAsync(c, s => s.PauseAsync(c.TaskId!.Value)),
            resume: c => _ = OperateAsync(c, s => s.ResumeAsync(c.TaskId!.Value)),
            cancel: c => _ = OperateAsync(c, s => s.CancelAsync(c.TaskId!.Value)),
            retry: c => _ = OperateAsync(c, s => s.StartAsync(c.TaskId!.Value)),
            priority: c => _ = ToggleEmergencyAsync(c),
            details: c => _ = ShowTaskFilesAsync(c),
            retryIdentify: c => _ = RetryIdentifyAsync(c),
            onSlotReleased: ReleaseSlot);

    private void ReleaseSlot(int slot)
    {
        _slotRouter.ReleaseSlot(slot);
        foreach (var kv in _deviceSlotCache.Where(kv => kv.Value == slot).ToList())
            _deviceSlotCache.TryRemove(kv.Key, out _);
    }

    #endregion

    #region 事件处理

    private void OnTaskProgressUpdated(object? sender, TaskProgressUpdatedEvent e)
        => _progressBuffer[e.TaskId] = new ProgressSnapshot(e.Progress, e.SpeedBytesPerSecond);

    private void FlushProgressBuffer()
    {
        if (_progressBuffer.IsEmpty) return;
        foreach (var kv in _progressBuffer)
        {
            if (!_progressBuffer.TryRemove(kv.Key, out var snap)) continue;
            var card = FindCardByTaskId(kv.Key);
            card?.UpdateProgress(snap.Percent, snap.SpeedBytesPerSecond);
        }
    }

    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEvent e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyStatus(e);
        else Dispatcher.UIThread.Post(() => ApplyStatus(e), DispatcherPriority.Normal);
    }

    private void ApplyStatus(TaskStatusChangedEvent e)
    {
        var card = FindCardByTaskId(e.TaskId);
        if (card is null)
        {
            // 未知任务：绑定到空闲卡片（Pooled 模式可能发生）
            card = ResolveOrAllocateCardForTask(e.TaskId, e.PhysicalPortId);
            if (card is null) return;
        }
        card.UpdateStatus(e.Status, e.IsEmergency, e.Version);
    }

    private void OnDeviceConnected(object? sender, DeviceConnectedEvent e)
    {
        Dispatcher.UIThread.Post(() => ApplyDeviceConnected(e), DispatcherPriority.Normal);
    }

    private void ApplyDeviceConnected(DeviceConnectedEvent e)
    {
        var card = ResolveCardForDevice(e.DeviceKey, e.PhysicalPortId, out var unmapped);
        if (card is null) return;

        card.ClearUnmapped();
        if (unmapped)
            card.SetUnmapped(e.PhysicalPortId);

        card.SetDeviceOnline(e.DeviceKey, e.DeviceName, e.Protocol);
    }

    private void OnDeviceDisconnected(object? sender, DeviceDisconnectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = FindCardByDeviceKey(e.DeviceKey);
            card?.SetDeviceOffline();
        }, DispatcherPriority.Normal);
    }

    private void OnDeviceBound(object? sender, DeviceBoundEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = FindCardByDeviceKey(e.DeviceKey);
            card?.SetBound();
        }, DispatcherPriority.Normal);
    }

    private void OnDeviceRejected(object? sender, DeviceRejectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = FindCardByDeviceKey(e.DeviceKey);
            card?.SetRejected(e.Reason);
        }, DispatcherPriority.Normal);
    }

    #endregion

    #region 卡片定位

    private UsbPortCardViewModel? FindCardByTaskId(long taskId)
    {
        var slot = _slotRouter.ResolveSlotForTask(taskId);
        if (slot is null) return null;
        return SlotInRange(slot.Value) ? PortCards[slot.Value] : null;
    }

    private UsbPortCardViewModel? FindCardByDeviceKey(string deviceKey)
    {
        if (_deviceSlotCache.TryGetValue(deviceKey, out var slot) && SlotInRange(slot))
            return PortCards[slot];

        var resolved = _slotRouter.ResolveSlotForDevice(deviceKey);
        if (resolved is null) return null;

        _deviceSlotCache[deviceKey] = resolved.Value;
        return SlotInRange(resolved.Value) ? PortCards[resolved.Value] : null;
    }

    /// <summary>
    /// 设备接入路由：
    /// 1) ISlotRouter 命中固定槽位 → 用该槽；
    /// 2) 未映射（Fixed 模式）→ 找空闲槽位显示 #??；调用方通过 unmapped=true 得知；
    /// 3) 无空闲 → 返回 null。
    /// </summary>
    private UsbPortCardViewModel? ResolveCardForDevice(
        string deviceKey, string? physicalPortId, out bool unmapped)
    {
        unmapped = false;

        // 1. ISlotRouter 命中
        var slot = _slotRouter.ResolveSlotForDevice(deviceKey);
        if (slot is not null && SlotInRange(slot.Value))
        {
            _deviceSlotCache[deviceKey] = slot.Value;
            return PortCards[slot.Value];
        }

        // 2. 未映射：找空闲卡片
        unmapped = true;
        for (var i = 0; i < PortCards.Count; i++)
        {
            var c = PortCards[i];
            if (c.TaskId is null && c.LinkState == DeviceLinkState.Offline)
            {
                _deviceSlotCache[deviceKey] = i;
                return c;
            }
        }

        // 3. 无空槽
        _logger.LogWarning("设备接入但无空槽位：{DeviceKey}", deviceKey);
        return null;
    }

    private UsbPortCardViewModel? ResolveOrAllocateCardForTask(long taskId, string? physicalPortId)
    {
        // 1. 已知映射
        var slot = _slotRouter.ResolveSlotForTask(taskId);
        if (slot is not null && SlotInRange(slot.Value))
            return PortCards[slot.Value];

        // 2. 未绑定：找空闲卡片（Pooled 模式兜底）
        for (var i = 0; i < PortCards.Count; i++)
        {
            var c = PortCards[i];
            if (c.TaskId is null)
            {
                _slotRouter.BindTask(taskId, i);
                return c;
            }
        }
        return null;
    }

    private bool SlotInRange(int slot) => slot >= 0 && slot < PortCards.Count;

    #endregion

    #region 快照刷新

    private async Task RefreshSnapshotAsync()
    {
        try
        {
            var snapshots = await _cardService.GetCurrentSnapshotAsync();
            foreach (var dto in snapshots)
            {
                UsbPortCardViewModel? card = null;
                bool unmapped = false;

                // 1. DeviceKey 命中
                if (!string.IsNullOrEmpty(dto.DeviceKey))
                    card = ResolveCardForDevice(dto.DeviceKey, dto.PhysicalPortId, out unmapped);

                // 2. TaskId 命中
                if (card is null && dto.TaskId is { } tid)
                {
                    var slot = _slotRouter.ResolveSlotForTask(tid);
                    if (slot is not null && SlotInRange(slot.Value))
                        card = PortCards[slot.Value];
                }

                if (card is null) continue;

                if (unmapped) card.SetUnmapped(dto.PhysicalPortId);
                card.UpdateFromDto(dto);

                if (dto.TaskId is { } boundTask)
                {
                    _slotRouter.BindTask(boundTask, card.SlotIndex);
                    card.BindTask(boundTask);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "快照刷新失败");
        }
    }

    #endregion

    #region 用户操作

    private async Task OperateAsync(UsbPortCardViewModel card, Func<ICollectTaskService, Task> action)
    {
        if (!CanOperate || card.TaskId is null) return;
        try { await action(_collectService); }
        catch (Exception ex) { _logger.LogWarning(ex, "卡片操作失败 slot={Slot}", card.SlotIndex); }
    }

    private async Task ToggleEmergencyAsync(UsbPortCardViewModel card)
    {
        if (!CanOperate || card.TaskId is null) return;
        try
        {
            var target = !card.IsEmergency;
            var ok = await _collectService.SetEmergencyAsync(card.TaskId.Value, target);
            if (ok) return;

            var owner = (Avalonia.Application.Current?.ApplicationLifetime
                         as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is null) return;

            await MessageDialog.ShowAsync(
                owner, "提示",
                $"紧急优先已达上限（{_collectOptions.MaxEmergencyTasks}），请先取消其他优先任务",
                MessageKind.Warning);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>识别拒绝后重试：调 IDevicePresenceService.RecheckAsync。</summary>
    private async Task RetryIdentifyAsync(UsbPortCardViewModel card)
    {
        if (!card.CanRetryIdentify) return;
        if (string.IsNullOrEmpty(card.DeviceKey)) return;

        card.CanRetryIdentify = false;
        try
        {
            await _presence.RecheckAsync(card.DeviceKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "重试识别失败 slot={Slot}", card.SlotIndex);
        }
    }

    #endregion

    #region 明细窗口

    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId) =>
        await _collectService.GetTaskFilesAsync(taskId);

    private async Task ShowTaskFilesAsync(UsbPortCardViewModel card)
    {
        if (card.TaskId is not { } taskId) return;

        var owner = (Avalonia.Application.Current?.ApplicationLifetime
                     as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;

        IReadOnlyList<CollectFileDto> files;
        try { files = await GetTaskFilesAsync(taskId); }
        catch { files = []; }

        var vm = new TaskFilesViewModel(
            taskId,
            $"{card.PortText} · {card.DeviceText}",
            files,
            _eventService);

        var window = new TaskFilesWindow { DataContext = vm };
        await Dispatcher.UIThread.InvokeAsync(() => window.Show(owner));
    }

    #endregion

    #region 会话 / 释放

    private void OnSessionChanged()
    {
        CanOperate = _sessions.IsAuthenticated &&
                     _operationAccess.HasPermission(_sessions.Current!, "collect");
    }

    public void Dispose()
    {
        _sessions.SessionChanged -= OnSessionChanged;
        _heartbeatTimer.Stop();
        _progressFlushTimer.Stop();
        _progressBuffer.Clear();
        _deviceSlotCache.Clear();

        _eventService.TaskProgressUpdated -= OnTaskProgressUpdated;
        _eventService.TaskStatusChanged -= OnTaskStatusChanged;
        _eventService.DeviceConnected -= OnDeviceConnected;
        _eventService.DeviceDisconnected -= OnDeviceDisconnected;
        _eventService.DeviceBound -= OnDeviceBound;
        _eventService.DeviceRejected -= OnDeviceRejected;

        foreach (var c in PortCards) c.Dispose();
    }

    #endregion
}
