using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Alerts;
using Station.Application.Audit;
using Station.Application.Authentication;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Recorders;
using Station.Application.Settings;
using Station.Application.Uploading;
using Station.Contracts;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Settings;
using Station.Desktop.Application.Session;
using Station.Desktop.WebHost.Settings;
using Station.Desktop.UI.Services;
using Station.Desktop.UI.ViewModels;
using Station.Domain.Entities;
using Station.Infrastructure.Repositories;

namespace Station.Desktop.UI.Views;

public partial class ShellWindow : Window
{
    private static readonly (string Key, string Title)[] ModuleCatalog =
    [
        ("workbench", "工作台"),
        ("collect", "采集作业"),
        ("history", "历史记录"),
        ("logs", "日志中心"),
        ("alerts", "报警中心"),
        ("settings", "设置")
    ];

    private readonly ISessionManager _sessions;
    private readonly IOperationAccessService _operationAccess;
    private readonly IAlertService _alertService;
    private readonly ILicenseService _licenseService;
    private readonly SessionIdleTracker _idleTracker;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _alertTimer;
    private int _clockTicks;
    private long? _lastAlertId;
    private readonly Dictionary<string, Button> _navButtons;
    private IDisposable? _currentViewModel;

    public ShellWindow()
    {
        InitializeComponent();

        var services = App.Services!;
        _sessions = services.GetRequiredService<ISessionManager>();
        _operationAccess = services.GetRequiredService<IOperationAccessService>();
        _alertService = services.GetRequiredService<IAlertService>();
        _licenseService = services.GetRequiredService<ILicenseService>();
        _sessions.SessionChanged += OnSessionChanged;

        _navButtons = new Dictionary<string, Button>
        {
            ["workbench"] = NavWorkbench,
            ["collect"] = NavCollect,
            ["history"] = NavHistory,
            ["logs"] = NavLogs,
            ["alerts"] = NavAlerts,
            ["settings"] = NavSettings
        };

        _idleTracker = new SessionIdleTracker(
            _sessions,
            services.GetRequiredService<AuthOptions>());
        _idleTracker.WarningChanged += OnIdleWarningChanged;
        _idleTracker.Attach(this);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += async (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (++_clockTicks % 60 == 0)
            {
                await RefreshLicenseAsync();
            }
        };
        _clockTimer.Start();

        _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _alertTimer.Tick += async (_, _) => await RefreshAlertBannerAsync();
        _alertTimer.Start();

        OnSessionChanged();
        _ = NavigateAsync("workbench");
        _ = RefreshLicenseAsync();
    }

    private void OnSessionChanged()
    {
        if (_sessions.Current is { } session)
        {
            SessionStatusText.Text = $"{session.Name ?? session.UserName}（{session.UserName}）";
            SessionStatusText.Foreground = Avalonia.Media.Brushes.White;
            LogoutButton.IsVisible = true;
        }
        else
        {
            SessionStatusText.Text = "未登录";
            SessionStatusText.Foreground = Avalonia.Media.Brushes.LightGray;
            LogoutButton.IsVisible = false;
        }
    }

    private async void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string moduleKey })
        {
            await NavigateAsync(moduleKey);
        }
    }

    private async Task NavigateAsync(string moduleKey)
    {
        // 必须登录模块：未登录时先弹登录窗
        if (_operationAccess.IsLoginRequired(moduleKey) && !_sessions.IsAuthenticated)
        {
            var loggedIn = await ShowLoginAsync();
            if (!loggedIn)
            {
                return;
            }
        }

        // 登录后仍需权限点校验
        if (_sessions.IsAuthenticated && !_operationAccess.HasPermission(_sessions.Current!, moduleKey))
        {
            ShowModule(moduleKey, "无权限访问", "当前账号无该模块操作权限，请联系管理员");
            return;
        }

        ShowModule(moduleKey, null, null);
    }

    private void ShowModule(string moduleKey, string? overrideTitle, string? overrideMessage)
    {
        UpdateNavSelection(moduleKey);
        _currentViewModel?.Dispose();
        _currentViewModel = null;

        if (moduleKey == "workbench" && overrideTitle is null)
        {
            var services = App.Services!;
            var viewModel = new WorkbenchViewModel(
                _sessions,
                services.GetRequiredService<ICollectTaskService>(),
                services.GetRequiredService<IUploadService>(),
                services.GetRequiredService<CollectOptions>(),
                services.GetRequiredService<IRepository<CollectFile>>(),
                services.GetRequiredService<ILicenseService>(),
                _operationAccess);
            ModuleContent.Content = new WorkbenchView { DataContext = viewModel };
            _currentViewModel = viewModel;
            return;
        }

        if (moduleKey == "collect" && overrideTitle is null)
        {
            var services = App.Services!;
            var viewModel = new CollectModuleViewModel(
                services.GetRequiredService<ICollectTaskService>(),
                services.GetRequiredService<IUploadService>(),
                services.GetRequiredService<IRecorderService>(),
                services.GetRequiredService<IRecorderIdentificationService>(),
                services.GetRequiredService<ICollectSource>(),
                _sessions);
            ModuleContent.Content = new CollectModuleView { DataContext = viewModel };
            _currentViewModel = viewModel;
            return;
        }

        if (moduleKey == "alerts" && overrideTitle is null)
        {
            var viewModel = new AlertModuleViewModel(_alertService, _sessions);
            ModuleContent.Content = new AlertModuleView { DataContext = viewModel };
            _currentViewModel = viewModel;
            return;
        }

        if (moduleKey == "history" && overrideTitle is null)
        {
            var services = App.Services!;
            var viewModel = new HistoryModuleViewModel(
                services.GetRequiredService<ICollectTaskService>());
            ModuleContent.Content = new HistoryModuleView { DataContext = viewModel };
            _currentViewModel = viewModel;
            return;
        }

        if (moduleKey == "logs" && overrideTitle is null)
        {
            var services = App.Services!;
            var viewModel = new LogsModuleViewModel(
                services.GetRequiredService<IAuditLogService>());
            ModuleContent.Content = new LogsModuleView { DataContext = viewModel };
            _currentViewModel = viewModel;
            return;
        }

        if (moduleKey == "settings" && overrideTitle is null)
        {
            var services = App.Services!;
            var viewModel = new SettingsModuleViewModel(
                services.GetRequiredService<ISystemSettingsService>(),
                services.GetRequiredService<INetworkSettingsService>(),
                services.GetRequiredService<ISystemSelfCheckService>(),
                services.GetRequiredService<ILicenseService>(),
                _sessions,
                _operationAccess);
            ModuleContent.Content = new SettingsModuleView { DataContext = viewModel };
            _currentViewModel = viewModel;
            return;
        }

        var title = overrideTitle ?? ModuleCatalog.First(m => m.Key == moduleKey).Title;
        var message = overrideMessage ?? "该模块正在开发中（M6 界面骨架）";
        ModuleContent.Content = new ModulePlaceholderView
        {
            DataContext = new ModulePlaceholderViewModel(title, message)
        };
    }

    private void UpdateNavSelection(string moduleKey)
    {
        foreach (var (key, button) in _navButtons)
        {
            button.Classes.Set("nav-pill-selected", key == moduleKey);
        }
    }

    private async Task<bool> ShowLoginAsync()
    {
        var services = App.Services!;
        var viewModel = new LoginViewModel(
            services.GetRequiredService<IAuthenticationService>(),
            _sessions);
        var dialog = new LoginDialog(viewModel, this);
        return await dialog.ShowDialog<bool>(this);
    }

    private async void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        if (_sessions.Current is { } session)
        {
            var auth = App.Services!.GetRequiredService<IAuthenticationService>();
            await auth.LogoutAsync(session.AccountId);
        }

        _sessions.Clear();
    }

    private void OnIdleWarningChanged(int remainingSeconds)
    {
        IdleWarningBar.IsVisible = remainingSeconds >= 0;
        IdleWarningText.Text = remainingSeconds > 0
            ? $"即将自动退出登录（{remainingSeconds} 秒），请操作以保持会话"
            : "即将自动退出登录，请操作以保持会话";
    }

    private async Task RefreshAlertBannerAsync()
    {
        try
        {
            var pending = await _alertService.CountPendingAsync();
            AlertBanner.IsVisible = pending > 0;
            AlertBannerText.Text = pending > 0 ? $"⚠ 有 {pending} 条待处理报警（点击查看）" : string.Empty;
            var latest = (await _alertService.GetAlertsAsync(null, AlertStatus.Pending, 5))
                .OrderByDescending(a => a.Id)
                .FirstOrDefault();
            if (latest is not null)
            {
                if (_lastAlertId is null)
                {
                    _lastAlertId = latest.Id;
                }
                else if (latest.Id > _lastAlertId)
                {
                    _lastAlertId = latest.Id;
                    Services.DesktopAlertSound.Play();
                    ShowAlertPopup(latest);
                }
            }
        }
        catch
        {
            // 忽略轮询异常
        }
    }

    private void ShowAlertPopup(AlertDto alert)
    {
        var popup = new AlertPopupWindow(
            $"{AlertTypeName(alert.Type)} · {LevelName(alert.Level)}",
            alert.Title,
            this);
        popup.Show();
    }

    private static string AlertTypeName(AlertType type) => type switch
    {
        AlertType.DiskLow => "磁盘不足",
        AlertType.NetworkDown => "网络中断",
        AlertType.UsbFault => "USB故障",
        AlertType.ChecksumFailed => "校验失败",
        AlertType.UnauthorizedAccess => "非授权接入",
        AlertType.BindingInvalid => "绑定异常",
        AlertType.StorageUnreachable => "存储不可达",
        AlertType.LicenseExpired => "授权到期",
        _ => "报警"
    };

    private static string LevelName(AlertLevel level) => level switch
    {
        AlertLevel.Warning => "警告",
        AlertLevel.Critical => "严重",
        _ => "提示"
    };

    private async void OnAlertBannerTapped(object? sender, TappedEventArgs e)
    {
        AlertBanner.IsVisible = false;
        await NavigateAsync("alerts");
    }

    private async Task RefreshLicenseAsync()
    {
        try
        {
            var check = await _licenseService.CheckAsync();
            (LicenseBadgeText.Text, LicenseBadge.Background) = check.Status switch
            {
                Station.Contracts.LicenseStatus.Activated => ($"正式版·剩余{check.DaysLeft}天", new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#14532d"))),
                Station.Contracts.LicenseStatus.Locked => ("授权已到期", new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7f1d1d"))),
                _ => ($"试用·剩余{check.DaysLeft}天", new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#92400e")))
            };
            LicenseBadgeText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#fef3c7"));
        }
        catch
        {
            // 忽略授权刷新异常
        }
    }
}
