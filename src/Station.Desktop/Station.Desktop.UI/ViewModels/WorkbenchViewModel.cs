using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Settings;
using Station.Application.Uploading;
using Station.Application.UsbPortCard;
using Station.Application.UsbPortCard.Events;
using Station.Desktop.Application.Monitoring;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Session;
using Station.Domain.Entities;
using Station.Domain.Enums;

namespace Station.Desktop.UI.ViewModels;

/// <summary>
/// 工作台：USB 采集通道卡片（行数/每行卡片数/卡片宽高可配置）+ 统计 + 本机监控。
/// 卡片实时反映各端口当前任务（采集中/已暂停/空闲），支持暂停/恢复/取消/重试/查看明细。
/// </summary>
public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly ICollectTaskService _collectService;
    private readonly IUploadService _uploadService;
    private readonly ILicenseService _license;
    private readonly IOperationAccessService _operationAccess;
    private readonly IUsbPortCardService _cardService;          // 查询服务（快照）
    private readonly IUsbPortCardEventService _eventService;    // 事件聚合器

    private readonly CollectOptions _collectOptions;
    private readonly WorkbenchOptions _workbench;
    private readonly SystemMonitorService _monitor;
    private readonly DispatcherTimer _heartbeatTimer;   // 兜底刷新（5秒）
    private readonly DispatcherTimer _monitorTimer;     // 系统监控定时器（2秒）
    private readonly DispatcherTimer _licenseTimer;     // 授权刷新（30秒）

    private int _monitorTicks;
    private int _licenseTicks;

    /// <summary>
    /// 端口卡片集合
    /// </summary>
    public ObservableCollection<UsbPortCardViewModel> PortCards { get; }

    // 布局配置
    [ObservableProperty]
    private int _columns = 5;

    [ObservableProperty]
    private double _cardWidth = 240;

    [ObservableProperty]
    private double _cardHeight = 200;

    // 状态栏
    [ObservableProperty]
    private string _onlineText = "加载中…";

    [ObservableProperty]
    private string _todayText = "—";

    [ObservableProperty]
    private string _pendingUploadText = "—";

    [ObservableProperty]
    private string _portStatsText = "采集中 0 · 已暂停 0 · 空闲 30";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // 系统监控
    [ObservableProperty]
    private string _cpuText = "—";

    [ObservableProperty]
    private string _memText = "—";

    [ObservableProperty]
    private string _diskText = "—";

    [ObservableProperty]
    private string _netText = "—";

    [ObservableProperty]
    private string _portsText = "—";

    [ObservableProperty]
    private string _deviceText = "—";

    [ObservableProperty]
    private bool _canOperate;

    public WorkbenchViewModel(
        ISessionManager sessions,
        ICollectTaskService collectService,
        IUploadService uploadService,
        ILicenseService license,
        IOperationAccessService operationAccess,
        IUsbPortCardService cardService,
        IUsbPortCardEventService eventService,
        CollectOptions collectOptions,
        WorkbenchOptions workbench)
    {
        _sessions = sessions;
        _collectService = collectService;
        _uploadService = uploadService;
        _license = license;
        _operationAccess = operationAccess;
        _cardService = cardService;
        _eventService = eventService;
        _collectOptions = collectOptions;
        _workbench = workbench;

        // 1. 订阅事件
        _eventService.TaskProgressUpdated += OnTaskProgressUpdated;
        _eventService.TaskStatusChanged += OnTaskStatusChanged;
        _eventService.DeviceConnected += OnDeviceConnected;
        _eventService.DeviceDisconnected += OnDeviceDisconnected;

        // 2. 初始化卡片集合（根据配置创建空卡片）
        PortCards = [];
        EnsureCardLayout();

        // 3. 会话权限
        _sessions.SessionChanged += OnSessionChanged;
        OnSessionChanged();
        // 4. 系统监控（每2秒刷新一次）
        _monitor = new SystemMonitorService(collectOptions);
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _monitorTimer.Tick += (_, _) => RefreshMonitor();
        _monitorTimer.Start();
        // 5. 授权刷新（每30秒）
        _licenseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _licenseTimer.Tick += async (_, _) => await RefreshLicenseAsync();
        _licenseTimer.Start();
        // 6. 心跳兜底（每5秒全量刷新）
        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _heartbeatTimer.Tick += async (_, _) => await RefreshSnapshotAsync();
        _heartbeatTimer.Start();

        // 7. 首次加载：获取今日统计、待上传数量
        _ = RefreshTodayAsync();
        _ = RefreshPendingUploadAsync();
        // 首次快照加载
        _ = RefreshSnapshotAsync();
        // 首次授权监控
        _ = RefreshLicenseAsync();
        // 首次系统监控
        RefreshMonitor();

    }

    // ---- 事件处理方法（UI线程调度） ----

    private void OnTaskProgressUpdated(object? sender, TaskProgressUpdatedEvent e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var card = FindCardByTaskId(e.TaskId);
            card?.UpdateProgress(e.Progress, e.SpeedBytesPerSecond);
        });
    }

    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEvent e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var card = FindCardByTaskId(e.TaskId);
            if (card is not null)
            {
                card.UpdateStatus(e.Status, e.IsEmergency);
                // 更新统计
                UpdatePortStats();
            }
        });
    }

    private void OnDeviceConnected(object? sender, DeviceConnectedEvent e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 找到第一个空闲卡片（IsIdle = true）绑定此设备
            var card = PortCards.FirstOrDefault(c => c.IsIdle && string.IsNullOrEmpty(c.DeviceKey));
            if (card is not null)
            {
                card.SetDeviceOnline(e.DeviceKey, e.DeviceName, e.Protocol);
            }
            // 没有空闲卡片则忽略（或记录日志）
        });
    }

    private void OnDeviceDisconnected(object? sender, DeviceDisconnectedEvent e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var card = FindCardByDeviceKey(e.DeviceKey);
            if (card is not null && card.IsIdle) // 只有空闲设备才清理，正在采集的不清理（由状态事件处理）
            {
                card.SetIdle();
                UpdatePortStats();
            }
        });
    }

    // ---- 辅助查找方法 ----

    private UsbPortCardViewModel? FindCardByTaskId(long taskId)
    {
        return PortCards.FirstOrDefault(c => c.TaskId == taskId);
    }

    private UsbPortCardViewModel? FindCardByDeviceKey(string deviceKey)
    {
        return PortCards.FirstOrDefault(c => c.TaskNo == deviceKey);
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
            // 更新统计
            UpdatePortStats();
        }
        catch
        {
            // 静默失败
        }
    }
    // ---- 今日统计 & 待上传 ----

    private async Task RefreshTodayAsync()
    {
        try
        {
            var stats = await _cardService.GetTodayStatsAsync();
            TodayText = $"{stats.FileCount} 个 · {FormatSize(stats.TotalBytes)}";
        }
        catch
        {
            TodayText = "—";
        }
    }

    private async Task RefreshPendingUploadAsync()
    {
        try
        {
            var count = await _uploadService.CountPendingUploadsAsync();
            PendingUploadText = count.ToString();
        }
        catch
        {
            PendingUploadText = "—";
        }
    }

    // ---- 端口统计 ----

    private void UpdatePortStats()
    {
        var collecting = PortCards.Count(c => c.IsCollecting);
        var paused = PortCards.Count(c => c.IsPaused);
        var idle = PortCards.Count(c => c.IsIdle);
        PortStatsText = $"采集中 {collecting} · 已暂停 {paused} · 空闲 {idle}";
    }

    // ---- 系统监控 ----

    private void RefreshMonitor()
    {
        var lines = _monitor.Snapshot();
        CpuText = ValueOf(lines, 0);
        MemText = ValueOf(lines, 1);
        DiskText = ValueOf(lines, 2);
        NetText = ValueOf(lines, 3);
        PortsText = ValueOf(lines, 4);
        DeviceText = ValueOf(lines, 5);
    }

    private static string ValueOf(IReadOnlyList<MonitorLine> lines, int index) =>
        lines.Count > index ? lines[index].Value : "—";

    // ---- 授权刷新 ----

    private async Task RefreshLicenseAsync()
    {
        try
        {
            var check = await _license.CheckAsync();
            OnlineText = check.Message;
        }
        catch
        {
            OnlineText = "授权状态未知";
        }
    }

    // ---- 卡片布局管理 ----

    /// <summary>按配置重建卡片网格（保留已有卡片的设备绑定）</summary>
    private void EnsureCardLayout()
    {
        var total = Math.Clamp(_workbench.Rows, 1, 10) * Math.Clamp(_workbench.Columns, 1, 10);
        var width = (double)Math.Clamp(_workbench.CardWidth, 120, 500);
        var height = (double)Math.Clamp(_workbench.CardHeight, 100, 400);

        if (PortCards.Count == total && Columns == _workbench.Columns && CardWidth == width && CardHeight == height)
            return;

        Columns = Math.Clamp(_workbench.Columns, 1, 10);
        CardWidth = width;
        CardHeight = height;

        var existing = PortCards.ToList();
        var rebuilt = new List<UsbPortCardViewModel>();
        for (var i = 1; i <= total; i++)
        {
            var card = i <= existing.Count ? existing[i - 1] : CreateCard(i);
            card.SetCardSize(width, height);
            rebuilt.Add(card);
        }

        PortCards.Clear();
        foreach (var card in rebuilt)
            PortCards.Add(card);

        UpdatePortStats();
    }

    private UsbPortCardViewModel CreateCard(int portIndex) => new(
        portIndex,
        pause: c => _ = OperateAsync(c, s => s.PauseAsync(c.TaskId!.Value)),
        resume: c => _ = OperateAsync(c, s => s.ResumeAsync(c.TaskId!.Value)),
        cancel: c => _ = OperateAsync(c, s => s.CancelAsync(c.TaskId!.Value)),
        retry: c => _ = OperateAsync(c, s => s.StartAsync(c.TaskId!.Value)),
        priority: c => _ = ToggleEmergencyAsync(c),
        CardWidth,
        CardHeight);

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
            StatusMessage = ok
                ? target ? $"已设为紧急优先（上限 {_collectOptions.MaxEmergencyTasks}）" : "已取消紧急优先"
                : $"紧急优先已达上限（{_collectOptions.MaxEmergencyTasks}），请先取消其他优先任务";
            // 状态变更会由事件更新，无需主动刷新
        }
        catch (Exception ex)
        {
            StatusMessage = $"操作失败：{ex.Message}";
        }
    }
    // ---- 其他公共方法 ----

    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId) =>
        await _collectService.GetTaskFilesAsync(taskId);

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
        _monitorTimer.Stop();
        _licenseTimer.Stop();
        _heartbeatTimer.Stop();
        _eventService.TaskProgressUpdated -= OnTaskProgressUpdated;
        _eventService.TaskStatusChanged -= OnTaskStatusChanged;
        _eventService.DeviceConnected -= OnDeviceConnected;
        _eventService.DeviceDisconnected -= OnDeviceDisconnected;
    }
}
