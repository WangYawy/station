using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Authentication;
using Station.Application.Collecting;
using Station.Desktop.Application.Session;
using Station.Domain.Enums;

namespace Station.Desktop.UI.ViewModels;

public sealed record DeviceItem(string Name, string State, string StateColor);

public sealed record QueueItem(string Device, string Task, string Progress, string State, string StateColor);

public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly IAuthenticationService _authentication;
    private readonly ICollectTaskService _collectService;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private string _onlineText = "28 / 30";

    [ObservableProperty]
    private string _todayText = "156";

    [ObservableProperty]
    private string _pendingUploadText = "12";

    [ObservableProperty]
    private string _systemText = "CPU 23% · 磁盘 320GB/1TB";

    public IReadOnlyList<DeviceItem> DevicePool { get; } =
    [
        new("记录仪A-009", "在线 · 采集正常", "#22c55e"),
        new("记录仪B-015", "在线 · 等待任务", "#2563eb"),
        new("记录仪C-021", "离线", "#94a3b8")
    ];

    public ObservableCollection<QueueItem> ActiveQueue { get; } = [];

    public WorkbenchViewModel(
        ISessionManager sessions,
        IAuthenticationService authentication,
        ICollectTaskService collectService)
    {
        _sessions = sessions;
        _authentication = authentication;
        _collectService = collectService;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshQueueAsync();
        _timer.Start();
    }

    private async Task RefreshQueueAsync()
    {
        try
        {
            var tasks = await _collectService.GetActiveTasksAsync();
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
