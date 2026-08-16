using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Uploading;
using Station.Desktop.Application.Monitoring;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Session;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Repositories;
using SqlSugar;

namespace Station.Desktop.UI.ViewModels;

/// <summary>
/// 工作台：30 路 USB 采集通道卡片 + 统计（本机运行/今日采集/待上传/端口状态）+ 本机监控。
/// 卡片实时反映各端口当前任务（采集中/已暂停/空闲），支持暂停/恢复/取消/重试/查看明细。
/// </summary>
public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    public const int PortCount = 30;

    private readonly ISessionManager _sessions;
    private readonly ICollectTaskService _collectService;
    private readonly IUploadService _uploadService;
    private readonly IRepository<CollectFile> _files;
    private readonly ILicenseService _license;
    private readonly IOperationAccessService _operationAccess;
    private readonly SystemMonitorService _monitor;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, long> _portTaskIds = [];
    private int _monitorTicks;
    private int _licenseTicks;

    public ObservableCollection<UsbPortCardViewModel> PortCards { get; }

    [ObservableProperty]
    private string _onlineText = "加载中…";

    [ObservableProperty]
    private string _todayText = "—";

    [ObservableProperty]
    private string _pendingUploadText = "—";

    [ObservableProperty]
    private string _portStatsText = "采集中 0 · 已暂停 0 · 空闲 30";

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
        CollectOptions collectOptions,
        IRepository<CollectFile> files,
        ILicenseService license,
        IOperationAccessService operationAccess)
    {
        _sessions = sessions;
        _collectService = collectService;
        _uploadService = uploadService;
        _files = files;
        _license = license;
        _operationAccess = operationAccess;
        _monitor = new SystemMonitorService(collectOptions);

        PortCards = new ObservableCollection<UsbPortCardViewModel>(
            Enumerable.Range(1, PortCount).Select(i => new UsbPortCardViewModel(
                i,
                c => _ = OperateAsync(c, s => s.PauseAsync(c.TaskId!.Value)),
                c => _ = OperateAsync(c, s => s.ResumeAsync(c.TaskId!.Value)),
                c => _ = OperateAsync(c, s => s.CancelAsync(c.TaskId!.Value)),
                c => _ = OperateAsync(c, s => s.StartAsync(c.TaskId!.Value)))));

        _sessions.SessionChanged += OnSessionChanged;
        OnSessionChanged();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) =>
        {
            if (++_monitorTicks % 2 == 0)
            {
                RefreshMonitor();
            }

            if (++_licenseTicks % 30 == 0)
            {
                await RefreshLicenseAsync();
            }

            if (_monitorTicks % 2 == 0)
            {
                await RefreshDataAsync();
            }
        };
        _timer.Start();
        RefreshMonitor();
        _ = RefreshLicenseAsync();
        _ = RefreshDataAsync();
    }

    private void OnSessionChanged()
    {
        CanOperate = _sessions.IsAuthenticated &&
                     _operationAccess.HasPermission(_sessions.Current, "collect");
    }

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

    private async Task RefreshDataAsync()
    {
        try
        {
            var tasks = await _collectService.GetActiveTasksAsync();
            RefreshPorts(tasks);
            await RefreshTodayAsync();
            PendingUploadText = (await _uploadService.CountPendingUploadsAsync()).ToString();
        }
        catch
        {
            // 数据刷新失败不阻塞界面
        }
    }

    private void RefreshPorts(IReadOnlyList<CollectTaskDto> tasks)
    {
        var byId = tasks.ToDictionary(t => t.TaskId, t => t);
        var collecting = 0;
        var paused = 0;

        // 1) 更新已占用端口：任务仍在运行则刷新，否则清空为空闲
        foreach (var (port, taskId) in _portTaskIds.ToList())
        {
            if (byId.TryGetValue(taskId, out var task))
            {
                PortCards[port - 1].UpdateFromTask(task);
                if (task.Status == CollectTaskStatus.Paused)
                {
                    paused++;
                }
                else
                {
                    collecting++;
                }
            }
            else
            {
                _portTaskIds.Remove(port);
                PortCards[port - 1].SetIdle();
            }
        }

        // 2) 新任务分配到第一个空闲端口（端口映射保持稳定）
        var assigned = _portTaskIds.Values.ToHashSet();
        foreach (var task in tasks.Where(t => !assigned.Contains(t.TaskId)))
        {
            var port = Enumerable.Range(1, PortCount).FirstOrDefault(p => !_portTaskIds.ContainsKey(p));
            if (port == 0)
            {
                break;
            }

            _portTaskIds[port] = task.TaskId;
            PortCards[port - 1].UpdateFromTask(task);
            if (task.Status == CollectTaskStatus.Paused)
            {
                paused++;
            }
            else
            {
                collecting++;
            }
        }

        PortStatsText = $"采集中 {collecting} · 已暂停 {paused} · 空闲 {PortCount - collecting - paused}";
    }

    private async Task RefreshTodayAsync()
    {
        try
        {
            var today = DateTime.Today;
            var rows = await _files.AsQueryable()
                .Where(f => f.Status == CollectFileStatus.Completed && f.CollectedAt >= today)
                .Select(f => new { f.Size })
                .ToListAsync();
            TodayText = $"{rows.Count} 个 · {FormatSize(rows.Sum(r => r.Size))}";
        }
        catch
        {
            TodayText = "—";
        }
    }

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

    private async Task OperateAsync(UsbPortCardViewModel card, Func<ICollectTaskService, Task> action)
    {
        if (!CanOperate || card.TaskId is null)
        {
            return;
        }

        try
        {
            await action(_collectService);
            await RefreshDataAsync();
        }
        catch
        {
            // 操作失败由采集服务写入状态
        }
    }

    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId) =>
        await _collectService.GetTaskFilesAsync(taskId);

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

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

    public void Dispose()
    {
        _sessions.SessionChanged -= OnSessionChanged;
        _timer.Stop();
    }
}
