using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Monitoring;
using Station.Application.OperationAccess;
using Station.Application.Session;
using Station.Application.Settings;
using Station.Application.Uploading;
using Station.Application.UsbPortCard;
using Station.Application.UsbPortCard.Events;
using Station.Desktop.Views;
using Station.Domain.Entities;
using Station.Domain.Enums;

namespace Station.Desktop.ViewModels;

/// <summary>
/// 工作台：USB 采集通道卡片（行数/每行卡片数/卡片宽高可配置）。
/// 卡片实时反映各端口当前任务（采集中/已暂停/空闲），支持暂停/恢复/取消/重试/查看明细。
/// </summary>
public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly ICollectTaskService _collectService;
    private readonly IOperationAccessService _operationAccess;
    private readonly IUsbPortCardService _cardService;
    private readonly IUsbPortCardEventService _eventService;
    private readonly WindowModeOptions _options;
    // 心跳
    private readonly DispatcherTimer _heartbeatTimer;
    private int _refreshing; // 0/1 标记，防止心跳重入

    // 进度事件
    private readonly ConcurrentDictionary<long, ProgressSnapshot> _progressBuffer = new();
    private readonly DispatcherTimer _progressFlushTimer;
    private readonly record struct ProgressSnapshot(double Percent, double SpeedBytesPerSecond);

    #region 卡片布局 
    public ObservableCollection<UsbPortCardViewModel> PortCards { get; }

    [ObservableProperty] private int _columns = 5;
    [ObservableProperty] private int _rows = 6;
    [ObservableProperty] private bool _canOperate;

    /// <summary>由 WorkbenchView 根据窗口尺寸计算得到的卡片宽度（px）</summary>
    [ObservableProperty] private double _cardWidth = 240;

    /// <summary>由 WorkbenchView 根据窗口尺寸计算得到的卡片高度（px）</summary>
    [ObservableProperty] private double _cardHeight = 80;
    /// <summary>由 View 计算的网格总高度，用于给 UniformGrid 一个确定约束</summary>
    [ObservableProperty] private double _gridHeight;

    /// <summary>卡片最小宽度（来自 WindowModeOptions 配置）</summary>
    public double MinCardWidth => _options.MinCardWidth;

    /// <summary>卡片最小高度（来自 WindowModeOptions 配置）</summary>
    public double MinCardHeight => _options.MinCardHeight;

    #endregion

    public WorkbenchViewModel(
        ISessionManager sessions,
        ICollectTaskService collectService,
        IOperationAccessService operationAccess,
        IUsbPortCardService cardService,
        IUsbPortCardEventService eventService,
        WindowModeOptions options)
    {
        _sessions = sessions;
        _collectService = collectService;
        _operationAccess = operationAccess;
        _cardService = cardService;
        _eventService = eventService;
        _options = options;

        Columns = Math.Clamp(options.Columns, 1, 10);
        Rows = Math.Clamp(options.Rows, 1, 10);
        // 卡片初始尺寸取最小宽高，后续由 View 按实际可用空间刷新
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

        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatTimer.Tick += async (_, _) =>
        {
            await RefreshSnapshotAsync();
            // 上一次快照尚未返回时跳过本次，避免堆积
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
            try { await RefreshSnapshotAsync(); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        };
        _heartbeatTimer.Start();

        // 进度书信
        _progressFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)  // 6~7 FPS，人眼足够
        };
        _progressFlushTimer.Tick += (_, _) => FlushProgressBuffer();
        _progressFlushTimer.Start();


        _ = RefreshSnapshotAsync();
    }

    #region  // ---- 事件处理方法（UI线程调度） ----

    // 高频进度：跨线程只写缓冲区，不触碰 UI
    private void OnTaskProgressUpdated(object? sender, TaskProgressUpdatedEvent e)
    {
        _progressBuffer[e.TaskId] = new ProgressSnapshot(e.Progress, e.SpeedBytesPerSecond);
    }

    // 定时合帧：在 UI 线程统一刷新，30 端口最多 6~7 次/s 更新
    private void FlushProgressBuffer()
    {
        if (_progressBuffer.IsEmpty) return;

        foreach (var kv in _progressBuffer)
        {
            if (_progressBuffer.TryRemove(kv.Key, out var snap))
            {
                FindCardByTaskId(kv.Key)?.UpdateProgress(snap.Percent, snap.SpeedBytesPerSecond);
            }
        }
    }

    // 状态事件：让服务端保证"只在变化时发布"，客户端直接更新（不需 Post 排队）
    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEvent e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyStatus(e);
        else
            Dispatcher.UIThread.Post(() => ApplyStatus(e), DispatcherPriority.Normal);
    }
    private void ApplyStatus(TaskStatusChangedEvent e) => FindCardByTaskId(e.TaskId)?.UpdateStatus(e.Status, e.IsEmergency);

    private void OnDeviceConnected(object? sender, DeviceConnectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // 找到第一个空闲卡片（IsIdle = true）绑定此设备
            var card = PortCards.FirstOrDefault(c => c.LinkState == DeviceLinkState.Offline);
            if (card is null) return; // 无空闲卡片：忽略（建议后续 logger.LogWarning）

            card.SetDeviceOnline(e.DeviceKey, e.DeviceName, e.Protocol);
        }, DispatcherPriority.Normal);
    }

    private void OnDeviceDisconnected(object? sender, DeviceDisconnectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var card = FindCardByDeviceKey(e.DeviceKey);
            card?.SetDeviceOffline();   // 无任务则一起清空；有任务则保留任务显示
        }, DispatcherPriority.Normal);
    }

    // 绑定成功 -> 更新为已绑定，准备采集
    private void OnDeviceBound(object? sender, DeviceBoundEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            FindCardByDeviceKey(e.DeviceKey)?.SetBound();
        }, DispatcherPriority.Normal);
    }

    // 绑定失败 -> 显示错误
    private void OnDeviceRejected(object? sender, DeviceRejectedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            FindCardByDeviceKey(e.DeviceKey)?.SetRejected(e.Reason);
        }, DispatcherPriority.Normal);
    }

    #endregion



    // ---- 辅助查找方法 ----

    private UsbPortCardViewModel? FindCardByTaskId(long taskId)
    {
        return PortCards.FirstOrDefault(c => c.TaskId == taskId);
    }

    private UsbPortCardViewModel? FindCardByDeviceKey(string deviceKey)
    {
        return PortCards.FirstOrDefault(c =>
            !string.IsNullOrEmpty(c.DeviceKey) &&
            string.Equals(c.DeviceKey, deviceKey, StringComparison.Ordinal));
    }
    // ---- 快照刷新（兜底） ----

    private async Task RefreshSnapshotAsync()
    {
        try
        {
            var snapshots = await _cardService.GetCurrentSnapshotAsync();
            // 更新所有卡片（仅当有变化）
            for (int i = 0; i < snapshots.Count && i < PortCards.Count; i++)
            {
                PortCards[i].UpdateFromDto(snapshots[i]);
            }
        }
        catch
        {
            // 静默失败
        }
    }
    // ---- 卡片布局管理 ----

    /// <summary>按配置重建卡片网格（保留已有卡片的设备绑定）</summary>
    private void EnsureCardLayout()
    {
        var total = Math.Clamp(Rows, 1, 10) * Math.Clamp(Columns, 1, 10);
        if (PortCards.Count == total) return;

        var existing = PortCards.ToList();
        var rebuilt = new List<UsbPortCardViewModel>(total);
        for (var i = 1; i <= total; i++)
        {
            var card = i <= existing.Count ? existing[i - 1] : CreateCard(i);
            rebuilt.Add(card);
        }
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

    // ---- 用户操作（命令） ----

    private async Task OperateAsync(UsbPortCardViewModel card, Func<ICollectTaskService, Task> action)
    {
        if (!CanOperate || card.TaskId is null)
            return;

        try
        {
            await action(_collectService);
            // 操作成功后，事件会驱动更新，无需主动刷新
        }
        catch
        {
            // 操作失败由采集服务写入状态，事件会同步
        }
    }

    private async Task ToggleEmergencyAsync(UsbPortCardViewModel card)
    {
        if (!CanOperate || card.TaskId is null)
            return;

        try
        {
            var target = !card.IsEmergency;
            var ok = await _collectService.SetEmergencyAsync(card.TaskId.Value, target);
            //StatusMessage = ok
            //    ? target ? $"已设为紧急优先（上限 {_collectOptions.MaxEmergencyTasks}）" : "已取消紧急优先"
            //    : $"紧急优先已达上限（{_collectOptions.MaxEmergencyTasks}），请先取消其他优先任务";
            // 状态变更会由事件更新，无需主动刷新
        }
        catch (Exception ex)
        {
            //StatusMessage = $"操作失败：{ex.Message}";
        }
    }
    // ---- 其他公共方法 ----

    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId) =>
        await _collectService.GetTaskFilesAsync(taskId);

    /// <summary>
    /// 打开某张卡片的文件明细窗口。
    /// </summary>
    private async Task ShowTaskFilesAsync(UsbPortCardViewModel card)
    {
        if (card.TaskId is not { } taskId) return;

        // 取 MainWindow 作为 owner
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
                     as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;

        IReadOnlyList<CollectFileDto> files;
        try { files = await GetTaskFilesAsync(taskId); }
        catch { files = []; }

        var window = new TaskFilesWindow($"{card.PortText} · {card.DeviceText}", files);

        await Dispatcher.UIThread.InvokeAsync(() => window.Show(owner));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:F1} {units[unit]}";
    }

    // ---- 会话权限 ----

    private void OnSessionChanged()
    {
        CanOperate = _sessions.IsAuthenticated &&
                     _operationAccess.HasPermission(_sessions.Current, "collect");
    }

    // ---- 释放资源 ----

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
}
