using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Authentication;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Uploading;
using Station.Desktop.Application.Monitoring;
using Station.Desktop.Application.Session;
using Station.Desktop.Infrastructure.Collecting;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Repositories;
using SqlSugar;

namespace Station.Desktop.UI.ViewModels;

public sealed record DeviceItem(string Name, string State, string StateColor);

public sealed record QueueItem(string Device, string Task, string Progress, string State, string StateColor);

public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly IAuthenticationService _authentication;
    private readonly ICollectTaskService _collectService;
    private readonly IUploadService _uploadService;
    private readonly SystemMonitorService _monitor;
    private readonly IRepository<CollectFile> _files;
    private readonly ILicenseService _license;
    private readonly IReadOnlyList<IRecorderDeviceDetector> _detectors;
    private readonly DispatcherTimer _timer;
    private int _monitorTicks;
    private int _licenseTicks;

    [ObservableProperty]
    private string _onlineText = "加载中…";

    [ObservableProperty]
    private string _todayText = "—";

    [ObservableProperty]
    private string _pendingUploadText = "12";

    [ObservableProperty]
    private string _systemText = "CPU 23% · 磁盘 320GB/1TB";

    public ObservableCollection<DeviceItem> DevicePool { get; } = [];

    public ObservableCollection<QueueItem> ActiveQueue { get; } = [];

    public ObservableCollection<MonitorLine> MonitorLines { get; } = [];

    public WorkbenchViewModel(
        ISessionManager sessions,
        IAuthenticationService authentication,
        ICollectTaskService collectService,
        IUploadService uploadService,
        CollectOptions collectOptions,
        IRepository<CollectFile> files,
        ILicenseService license,
        IEnumerable<IRecorderDeviceDetector> detectors)
    {
        _sessions = sessions;
        _authentication = authentication;
        _collectService = collectService;
        _uploadService = uploadService;
        _files = files;
        _license = license;
        _detectors = detectors.ToList();
        _monitor = new SystemMonitorService(collectOptions);
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

            await RefreshQueueAsync();
            await RefreshDevicePoolAsync();
        };
        _timer.Start();
        _ = RefreshLicenseAsync();
        _ = RefreshTodayAsync();
    }

    private void RefreshMonitor()
    {
        var lines = _monitor.Snapshot();
        MonitorLines.Clear();
        foreach (var line in lines)
        {
            MonitorLines.Add(line);
        }

        SystemText = string.Join(" · ", lines.Take(3).Select(l => $"{l.Label} {l.Value}"));
    }

    private async Task RefreshQueueAsync()
    {
        try
        {
            var tasks = await _collectService.GetActiveTasksAsync();
            PendingUploadText = (await _uploadService.CountPendingUploadsAsync()).ToString();
            ActiveQueue.Clear();
            foreach (var task in tasks)
            {
                ActiveQueue.Add(new QueueItem(
                    task.RecorderName,
                    task.TaskNo,
                    $"{task.CollectedFiles}/{task.TotalFiles}",
                    CollectTaskStatusText.Of(task.Status),
                    QueueStatusColor(task.Status)));
            }
        }
        catch
        {
            // 采集服务暂不可用时忽略
        }
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

    private async Task RefreshDevicePoolAsync()
    {
        try
        {
            var tasks = await _collectService.GetActiveTasksAsync();
            var busy = tasks.Select(t => t.RecorderName).ToHashSet();
            DevicePool.Clear();
            foreach (var task in tasks)
            {
                DevicePool.Add(new DeviceItem(
                    task.RecorderName,
                    CollectTaskStatusText.Of(task.Status),
                    QueueStatusColor(task.Status)));
            }

            foreach (var device in _detectors.SelectMany(d => d.Detect()))
            {
                if (!busy.Contains(device.Name))
                {
                    DevicePool.Add(new DeviceItem(device.Name, "已连接 · 待采集", "#2563eb"));
                }
            }

            if (DevicePool.Count == 0)
            {
                DevicePool.Add(new DeviceItem("暂无设备接入", "—", "#94a3b8"));
            }
        }
        catch
        {
            // 设备池刷新失败不阻塞
        }
    }

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

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (_sessions.Current is { } session)
        {
            await _authentication.LogoutAsync(session.AccountId);
        }

        _sessions.Clear();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private static string QueueStatusColor(CollectTaskStatus status) => status switch
    {
        CollectTaskStatus.Collecting or CollectTaskStatus.Scanning => "#2563eb",
        CollectTaskStatus.Paused => "#f59e0b",
        _ => "#94a3b8"
    };
}
