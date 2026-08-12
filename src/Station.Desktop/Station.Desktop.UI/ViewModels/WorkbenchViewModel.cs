using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Authentication;
using Station.Desktop.Application.Session;

namespace Station.Desktop.UI.ViewModels;

public sealed record DeviceItem(string Name, string State, string StateColor);

public sealed record QueueItem(string Device, string Task, string Progress, string State, string StateColor);

public partial class WorkbenchViewModel : ObservableObject
{
    private readonly ISessionManager _sessions;
    private readonly IAuthenticationService _authentication;

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

    public IReadOnlyList<QueueItem> CollectQueue { get; } =
    [
        new("记录仪A-009", "文件任务", "80%", "同步中", "#2563eb"),
        new("记录仪B-015", "文件任务", "100%", "已同步", "#22c55e"),
        new("记录仪C-021", "文件任务", "—", "失败", "#ef4444")
    ];

    public WorkbenchViewModel(ISessionManager sessions, IAuthenticationService authentication)
    {
        _sessions = sessions;
        _authentication = authentication;
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
}
