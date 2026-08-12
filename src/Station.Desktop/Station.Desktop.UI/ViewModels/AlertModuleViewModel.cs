using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Alerts;
using Station.Contracts;
using Station.Desktop.Application.Session;

namespace Station.Desktop.UI.ViewModels;

public sealed record AlertItemViewModel(
    long Id,
    string Title,
    string? Detail,
    string? Source,
    string LevelText,
    string LevelColor,
    string StatusText,
    string StatusColor,
    string TimeText);

public partial class AlertModuleViewModel : ObservableObject, IDisposable
{
    private readonly IAlertService _alerts;
    private readonly ISessionManager _sessions;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<AlertItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _levelFilter = "all";

    [ObservableProperty]
    private string _statusFilter = "all";

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private AlertItemViewModel? _selectedItem;

    public bool CanHandle => _sessions.HasPermission("alert:handle");

    public AlertModuleViewModel(IAlertService alerts, ISessionManager sessions)
    {
        _alerts = alerts;
        _sessions = sessions;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
    }

    partial void OnLevelFilterChanged(string value) => _ = RefreshAsync();

    partial void OnStatusFilterChanged(string value) => _ = RefreshAsync();

    [RelayCommand]
    private void SetLevelFilter(string value) => LevelFilter = value;

    [RelayCommand]
    private void SetStatusFilter(string value) => StatusFilter = value;

    [RelayCommand]
    private async Task ConfirmAsync() => await SetStatusAsync(SelectedItem, AlertStatus.Confirmed);

    [RelayCommand]
    private async Task ProcessAsync() => await SetStatusAsync(SelectedItem, AlertStatus.Processed);

    [RelayCommand]
    private async Task CloseAsync() => await SetStatusAsync(SelectedItem, AlertStatus.Closed);

    private async Task SetStatusAsync(AlertItemViewModel? item, AlertStatus status)
    {
        if (item is null || !CanHandle)
        {
            return;
        }

        var ok = await _alerts.SetStatusAsync(item.Id, status);
        Message = ok ? "状态已更新" : "报警不存在";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var level = LevelFilter switch
            {
                "warning" => AlertLevel.Warning,
                "critical" => AlertLevel.Critical,
                _ => (AlertLevel?)null
            };
            var status = StatusFilter switch
            {
                "pending" => AlertStatus.Pending,
                "processed" => AlertStatus.Processed,
                _ => (AlertStatus?)null
            };

            var list = await _alerts.GetAlertsAsync(level, status, 100);
            Items.Clear();
            foreach (var alert in list)
            {
                Items.Add(new AlertItemViewModel(
                    alert.Id,
                    alert.Title,
                    alert.Detail,
                    alert.Source,
                    LevelText(alert.Level),
                    LevelColor(alert.Level),
                    StatusText(alert.Status),
                    StatusColor(alert.Status),
                    alert.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            }
        }
        catch (Exception ex)
        {
            Message = $"刷新失败：{ex.Message}";
        }
    }

    public void Dispose() => _timer.Stop();

    private static string LevelText(AlertLevel level) => level switch
    {
        AlertLevel.Info => "信息",
        AlertLevel.Warning => "警告",
        AlertLevel.Critical => "严重",
        _ => level.ToString()
    };

    private static string LevelColor(AlertLevel level) => level switch
    {
        AlertLevel.Critical => "#ef4444",
        AlertLevel.Warning => "#f59e0b",
        _ => "#2563eb"
    };

    private static string StatusText(AlertStatus status) => status switch
    {
        AlertStatus.Pending => "待处理",
        AlertStatus.Confirmed => "已确认",
        AlertStatus.Processed => "已处理",
        AlertStatus.Closed => "已关闭",
        _ => status.ToString()
    };

    private static string StatusColor(AlertStatus status) => status switch
    {
        AlertStatus.Pending => "#ef4444",
        AlertStatus.Confirmed => "#f59e0b",
        AlertStatus.Processed => "#22c55e",
        _ => "#94a3b8"
    };
}
