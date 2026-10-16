using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting;
using Station.Application.Collecting.Dtos;
using Station.Application.Notifications;
using Station.Application.OperationAccess;
using Station.Application.Session;
using Station.Application.Settings;
using Station.Application.UsbPortCards;
using Station.Contracts;
using Station.Desktop.Views;

namespace Station.Desktop.ViewModels;

public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly ICollectTaskService _collectService;
    private readonly IOperationAccessService _operationAccess;
    private readonly IUsbPortCardService _cardService;
    private readonly IUiNotificationService _eventService;
    private readonly IUiNotificationService _fileEvents;   // 明细窗口转发用
    private readonly WindowModeOptions _options;
    private readonly CollectOptions _collectOptions;         // 新增

    private readonly DispatcherTimer _heartbeatTimer;
    private int _refreshing;

    private readonly ConcurrentDictionary<long, ProgressSnapshot> _progressBuffer = new();
    private readonly DispatcherTimer _progressFlushTimer;
    private readonly record struct ProgressSnapshot(double Percent, double SpeedBytesPerSecond);

    #region 卡片布局
    public ObservableCollection<UsbPortCardViewModel> PortCards { get; }

    [ObservableProperty] private int _columns = 4;
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
        IUiNotificationService eventService,
        WindowModeOptions options,
        CollectOptions collectOptions)
    {
        _sessions = sessions;
        _collectService = collectService;
        _operationAccess = operationAccess;
        _cardService = cardService;
        _eventService = eventService;
        _fileEvents = eventService;
        _options = options;
        _collectOptions = collectOptions;

        Columns = Math.Clamp(options.Columns, 1, 10);
        Rows = Math.Clamp(options.Rows, 1, 10);
        CardWidth = options.MinCardWidth;
        CardHeight = options.MinCardHeight;

        _eventService.TaskProgressUpdated += OnTaskProgressUpdated;
        _eventService.TaskStatusChanged += OnTaskStatusChanged;
        _eventService.DeviceConnected += OnDeviceConnected;
        _eventService.DeviceDisconnected += OnDeviceDisconnected;
        _eventService.DeviceBound += OnDeviceBound;
        _eventService.DeviceRejected += OnDeviceRejected;

        PortCards = [];
        EnsureCardLayout();

        _sessions.SessionChanged += OnSessionChanged;
        OnSessionChanged();

        // ============ 心跳（修正重入保护顺序） ============
        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatTimer.Tick += async (_, _) =>
        {
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
            try { await RefreshSnapshotAsync(); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        };
        _heartbeatTimer.Start();

        _progressFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _progressFlushTimer.Tick += (_, _) => FlushProgressBuffer();
        _progressFlushTimer.Start();

        _ = RefreshSnapshotAsync();
    }

    #region 事件处理

    private void OnTaskProgressUpdated(object? sender, TaskProgressUpdatedEvent e)
        => _progressBuffer[e.TaskId] = new ProgressSnapshot(e.Progress, e.SpeedBytesPerSecond);

    private void FlushProgressBuffer()
    {
        if (_progressBuffer.IsEmpty) return;
        foreach (var kv in _progressBuffer)
        {
            if (_progressBuffer.TryRemove(kv.Key, out var snap))
                FindCardByTaskId(kv.Key)?.UpdateProgress(snap.Percent, snap.SpeedBytesPerSecond);
        }
    }

    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEvent e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyStatus(e);
        else Dispatcher.UIThread.Post(() => ApplyStatus(e), DispatcherPriority.Normal);
    }

    private void ApplyStatus(TaskStatusChangedEvent e)
        => FindCardByTaskId(e.TaskId)?.UpdateStatus(e.Status, e.IsEmergency);

    private void OnDeviceConnected(object? sender, DeviceConnectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = PortCards.FirstOrDefault(c =>
                c.LinkState == DeviceLinkState.Offline && c.TaskId is null);
            card?.SetDeviceOnline(e.DeviceKey, e.DeviceName, e.Protocol);
        }, DispatcherPriority.Normal);
    }

    private void OnDeviceDisconnected(object? sender, DeviceDisconnectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
            FindCardByDeviceKey(e.DeviceKey)?.SetDeviceOffline(),
            DispatcherPriority.Normal);
    }

    private void OnDeviceBound(object? sender, DeviceBoundEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            FindCardByDeviceKey(e.DeviceKey)?.SetBound();
            // 自动恢复/新建：不动（由 RecorderConnectMonitor 的 AutoCollectOnConnect 处理）
        }, DispatcherPriority.Normal);
    }

    private void OnDeviceRejected(object? sender, DeviceRejectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
            FindCardByDeviceKey(e.DeviceKey)?.SetRejected(e.Reason),
            DispatcherPriority.Normal);
    }

    #endregion

    #region 查找

    private UsbPortCardViewModel? FindCardByTaskId(long taskId)
        => PortCards.FirstOrDefault(c => c.TaskId == taskId);

    private UsbPortCardViewModel? FindCardByDeviceKey(string deviceKey)
        => PortCards.FirstOrDefault(c =>
            !string.IsNullOrEmpty(c.DeviceKey) &&
            string.Equals(c.DeviceKey, deviceKey, StringComparison.Ordinal));

    /// <summary>按 DeviceKey → TaskId → 空槽 三级匹配</summary>
    private UsbPortCardViewModel? MatchCard(UsbPortCardDto dto)
    {
        if (!string.IsNullOrEmpty(dto.DeviceKey))
        {
            var byKey = PortCards.FirstOrDefault(c =>
                !string.IsNullOrEmpty(c.DeviceKey) &&
                string.Equals(c.DeviceKey, dto.DeviceKey, StringComparison.Ordinal));
            if (byKey is not null) return byKey;
        }

        if (dto.TaskId is { } tid)
        {
            var byTask = PortCards.FirstOrDefault(c => c.TaskId == tid);
            if (byTask is not null) return byTask;
        }

        if (dto.IsConnected)
        {
            return PortCards.FirstOrDefault(c =>
                c.LinkState == DeviceLinkState.Offline && c.TaskId is null);
        }
        return null;
    }

    #endregion

    #region 快照刷新（按 Key 匹配）

    private async Task RefreshSnapshotAsync()
    {
        try
        {
            var snapshots = await _cardService.GetCurrentSnapshotAsync();
            foreach (var dto in snapshots)
                MatchCard(dto)?.UpdateFromDto(dto);
        }
        catch
        {
            // 静默失败
        }
    }

    #endregion

    #region 卡片布局

    private void EnsureCardLayout()
    {
        var total = Math.Clamp(Rows, 1, 10) * Math.Clamp(Columns, 1, 10);
        if (PortCards.Count == total) return;

        var existing = PortCards.ToList();
        var rebuilt = new List<UsbPortCardViewModel>(total);
        for (var i = 1; i <= total; i++)
            rebuilt.Add(i <= existing.Count ? existing[i - 1] : CreateCard(i));

        PortCards.Clear();
        foreach (var c in rebuilt) PortCards.Add(c);
    }

    private UsbPortCardViewModel CreateCard(int portIndex) => new(
        portIndex,
        pause: c => _ = OperateAsync(c, s => s.PauseAsync(c.TaskId!.Value)),
        resume: c => _ = OperateAsync(c, s => s.ResumeAsync(c.TaskId!.Value)),
        cancel: c => _ = OperateAsync(c, s => s.CancelAsync(c.TaskId!.Value)),
        retry: c => _ = OperateAsync(c, s => s.StartAsync(c.TaskId!.Value)),
        priority: c => _ = ToggleEmergencyAsync(c),
        details: c => _ = ShowTaskFilesAsync(c));

    #endregion

    #region 用户操作

    private async Task OperateAsync(UsbPortCardViewModel card, Func<ICollectTaskService, Task> action)
    {
        if (!CanOperate || card.TaskId is null) return;
        try { await action(_collectService); }
        catch { /* 由事件刷新 */ }
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
            _fileEvents);

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

        _eventService.TaskProgressUpdated -= OnTaskProgressUpdated;
        _eventService.TaskStatusChanged -= OnTaskStatusChanged;
        _eventService.DeviceConnected -= OnDeviceConnected;
        _eventService.DeviceDisconnected -= OnDeviceDisconnected;
        _eventService.DeviceBound -= OnDeviceBound;
        _eventService.DeviceRejected -= OnDeviceRejected;
    }

    #endregion
}
