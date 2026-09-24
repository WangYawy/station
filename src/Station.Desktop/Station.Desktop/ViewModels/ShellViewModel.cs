using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Alerts;
using Station.Application.Audit;
using Station.Application.Authentication;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Monitoring;
using Station.Application.OperationAccess;
using Station.Application.Recorders;
using Station.Application.Security;
using Station.Application.Session;
using Station.Application.Settings;
using Station.Application.Uploading;
using Station.Application.UsbPortCard;
using Station.Application.UsbPortCard.Events;
using Station.Contracts;
using Station.Desktop.Services;

namespace Station.Desktop.ViewModels;

/// <summary>底栏健康状态三态。</summary>
public enum BarHealth { Ok, Warning, Down }

public partial class ShellViewModel : ObservableObject, IDisposable
{
    #region 构造初始化
    private static readonly (string Key, string Title)[] ModuleCatalog =
    [
        ("workbench", "工作台"),
        ("data", "数据管理"),
        ("logs", "日志中心"),
        ("settings", "设置")
    ];

    private readonly IServiceProvider _services;
    private readonly ISessionManager _sessions;
    private readonly IOperationAccessService _operationAccess;
    private readonly IAlertService _alertService;
    private readonly ILicenseService _licenseService;
    private readonly IUploadService _uploadService;
    private readonly ICollectTaskService _collectService;

    private readonly SessionIdleTracker _idleTracker; // 会话空闲追踪
    private readonly SystemMonitorService _monitor; // 本机状态监控
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _alertTimer;
    private readonly DispatcherTimer _statusBarTimer;

    private long? _lastAlertId;
    private LogsModuleViewModel? _logsViewModel; // 日志页缓存，避免每次打开都重新加载
    private IDisposable? _currentModuleDisposable;

    private Func<Task<bool>>? _loginRequestHandler;

    public ShellViewModel(IServiceProvider services)
    {
        _services = services;
        _sessions = services.GetRequiredService<ISessionManager>();
        _operationAccess = services.GetRequiredService<IOperationAccessService>();
        _alertService = services.GetRequiredService<IAlertService>();
        _licenseService = services.GetRequiredService<ILicenseService>();
        _uploadService = services.GetRequiredService<IUploadService>();
        _collectService = services.GetRequiredService<ICollectTaskService>();

        _sessions.SessionChanged += OnSessionChanged;
        _licenseService.LicenseChanged += OnLicenseChanged;

        _idleTracker = new SessionIdleTracker(
            _sessions,
            services.GetRequiredService<AuthOptions>());
        _idleTracker.WarningChanged += OnIdleWarningChanged;

        _monitor = new SystemMonitorService(services.GetRequiredService<CollectOptions>());

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clockTimer.Start();

        _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _alertTimer.Tick += async (_, _) => await RefreshAlertBannerAsync();
        _alertTimer.Start();

        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarTimer.Tick += async (_, _) => await RefreshStatusBarAsync();
        _statusBarTimer.Start();

        BarIpText = GetLocalIpv4();

        UpdateThemeIcon();
        UpdateSessionUi();

        _ = RefreshLicenseAsync();
        _ = RefreshStatusBarAsync();
        _ = RefreshAlertBannerAsync();
        _ = NavigateAsync("workbench");
    }
    #endregion
    /// <summary>窗口构造后调用，把空闲追踪挂到 Window 上。</summary>
    public void AttachIdleTracker(Window window) => _idleTracker.Attach(window);

    /// <summary>注册"弹登录对话框"的 View 层回调。</summary>
    public void RegisterLoginRequest(Func<Task<bool>> handler) => _loginRequestHandler = handler;

    private Task<bool> InvokeLoginRequestAsync()
        => _loginRequestHandler?.Invoke() ?? Task.FromResult(false);

    // ===================== 导航 =====================
    [ObservableProperty] private string _currentModuleKey = "workbench";
    [ObservableProperty] private object? _currentModule;

    public bool IsWorkbenchSelected => CurrentModuleKey == "workbench";
    public bool IsDataSelected => CurrentModuleKey == "data";
    public bool IsLogsSelected => CurrentModuleKey == "logs";
    public bool IsSettingsSelected => CurrentModuleKey == "settings";

    partial void OnCurrentModuleKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsWorkbenchSelected));
        OnPropertyChanged(nameof(IsDataSelected));
        OnPropertyChanged(nameof(IsLogsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    [RelayCommand]
    private async Task NavigateAsync(string moduleKey)
    {
        if (_operationAccess.IsLoginRequired(moduleKey) && !_sessions.IsAuthenticated)
        {
            if (!await InvokeLoginRequestAsync())
                return;
        }

        if (_sessions.IsAuthenticated &&
            !_operationAccess.HasPermission(_sessions.Current!, moduleKey))
        {
            ShowModule(moduleKey, "无权限访问", "当前账号无该模块操作权限，请联系管理员");
            return;
        }

        ShowModule(moduleKey, null, null);
    }

    private void ShowModule(string moduleKey, string? overrideTitle, string? overrideMessage)
    {
        CurrentModuleKey = moduleKey;
        _currentModuleDisposable?.Dispose();
        _currentModuleDisposable = null;

        if (overrideTitle is null && moduleKey == "workbench")
        {
            var vm = new WorkbenchViewModel(
                _sessions,
                _collectService,
                _operationAccess,
                _services.GetRequiredService<IUsbPortCardService>(),
                _services.GetRequiredService<IUsbPortCardEventService>(),
                _services.GetRequiredService<WindowModeOptions>());
            _currentModuleDisposable = vm as IDisposable;
            CurrentModule = vm;
            return;
        }

        if (overrideTitle is null && moduleKey == "collect")
        {
            var vm = new CollectModuleViewModel(
                _services.GetRequiredService<ICollectTaskService>(),
                _services.GetRequiredService<IUploadService>(),
                _services.GetRequiredService<IRecorderService>(),
                _services.GetRequiredService<IRecorderIdentificationService>(),
                _sessions);
            _currentModuleDisposable = vm as IDisposable;
            CurrentModule = vm;
            return;
        }

        if (overrideTitle is null && moduleKey == "alerts")
        {
            var vm = new AlertModuleViewModel(_alertService, _sessions);
            _currentModuleDisposable = vm as IDisposable;
            CurrentModule = vm;
            return;
        }

        if (overrideTitle is null && moduleKey == "data")
        {
            var vm = new HistoryModuleViewModel(
                _services.GetRequiredService<ICollectTaskService>());
            _currentModuleDisposable = vm as IDisposable;
            CurrentModule = vm;
            return;
        }

        if (overrideTitle is null && moduleKey == "logs")
        {
            _logsViewModel ??= new LogsModuleViewModel(
                _services.GetRequiredService<IAuditLogService>());
            CurrentModule = _logsViewModel;
            return;
        }

        if (overrideTitle is null && moduleKey == "settings")
        {
            var vm = new SettingsModuleViewModel(
                _services.GetRequiredService<ISystemSettingsService>(),
                _services.GetRequiredService<ISystemSelfCheckService>(),
                _services.GetRequiredService<ILicenseService>(),
                _sessions,
                _operationAccess,
                _services.GetRequiredService<ICryptoPolicyService>(),
                _services.GetRequiredService<IAuditLogService>());
            _currentModuleDisposable = vm as IDisposable;
            CurrentModule = vm;
            return;
        }

        var title = overrideTitle
            ?? ModuleCatalog.FirstOrDefault(m => m.Key == moduleKey).Title
            ?? "未知模块";
        var message = overrideMessage ?? "该模块正在开发中";
        CurrentModule = new ModulePlaceholderViewModel(title, message);
    }

    // ===================== 会话 =====================
    [ObservableProperty] private string _sessionDisplayText = "未登录";
    [ObservableProperty] private IBrush _sessionTextBrush = Brushes.LightGray;

    public bool IsAuthenticated => _sessions.IsAuthenticated;

    private void OnSessionChanged() => Dispatcher.UIThread.Post(UpdateSessionUi);

    private void UpdateSessionUi()
    {
        OnPropertyChanged(nameof(IsAuthenticated));

        if (_sessions.Current is { } s)
        {
            SessionDisplayText = $"{s.Name ?? s.UserName}（{s.UserName}）";
            SessionTextBrush = Brushes.White;
        }
        else
        {
            SessionDisplayText = "未登录";
            SessionTextBrush = Brushes.LightGray;
            IsNoticeBarVisible = true;
        }
    }

    /// <summary>供 View 层 session-badge 未登录分支调用。</summary>
    public Task<bool> RaiseLoginRequestAsync() => InvokeLoginRequestAsync();

    /// <summary>由 View 层确认后调用。</summary>
    public async Task LogoutAsync()
    {
        if (_sessions.Current is { } session)
        {
            var auth = _services.GetRequiredService<IAuthenticationService>();
            await auth.LogoutAsync(session.AccountId);
        }

        _sessions.Clear();
        await NavigateAsync("workbench");
    }

    // ===================== 主题 =====================
    [ObservableProperty] private Geometry? _themeIcon;

    [RelayCommand]
    private void ToggleTheme()
    {
        var app = Avalonia.Application.Current!;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        var isDark = Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        ThemeIcon = (Geometry?)Avalonia.Application.Current!.FindResource(isDark ? "IconSun" : "IconMoon");
    }

    // ===================== 公告栏 =====================
    [ObservableProperty] private bool _isNoticeBarVisible = true;
    [RelayCommand] private void CloseNoticeBar() => IsNoticeBarVisible = false;

    // ===================== 空闲提醒 =====================
    [ObservableProperty] private bool _isIdleWarningVisible;
    [ObservableProperty] private string _idleWarningText = "";

    private void OnIdleWarningChanged(int remaining)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsIdleWarningVisible = remaining >= 0;
            IdleWarningText = remaining > 0
                ? $"即将自动退出登录（{remaining} 秒），请操作以保持会话"
                : "即将自动退出登录，请操作以保持会话";
        });
    }

    // ===================== 底栏状态 =====================
    [ObservableProperty] private string _clockText = "";

    [ObservableProperty] private string _barIpText = "—";

    [ObservableProperty] private string _barNetStateText = "网络正常";
    [ObservableProperty] private BarHealth _networkHealth = BarHealth.Ok;
    public bool IsNetOk => NetworkHealth == BarHealth.Ok;
    public bool IsNetWarn => NetworkHealth == BarHealth.Warning;
    public bool IsNetDown => NetworkHealth == BarHealth.Down;

    partial void OnNetworkHealthChanged(BarHealth value)
    {
        OnPropertyChanged(nameof(IsNetOk));
        OnPropertyChanged(nameof(IsNetWarn));
        OnPropertyChanged(nameof(IsNetDown));
    }

    [ObservableProperty] private string _barHeartbeatText = "心跳正常";
    [ObservableProperty] private BarHealth _heartbeatHealth = BarHealth.Ok;
    public bool IsHbOk => HeartbeatHealth == BarHealth.Ok;
    public bool IsHbWarn => HeartbeatHealth == BarHealth.Warning;
    public bool IsHbDown => HeartbeatHealth == BarHealth.Down;

    partial void OnHeartbeatHealthChanged(BarHealth value)
    {
        OnPropertyChanged(nameof(IsHbOk));
        OnPropertyChanged(nameof(IsHbWarn));
        OnPropertyChanged(nameof(IsHbDown));
    }

    [ObservableProperty] private string _barCpuText = "—";
    [ObservableProperty] private string _barMemText = "—";
    [ObservableProperty] private string _barDiskText = "—";
    [ObservableProperty] private string _barOnlineText = "—";
    [ObservableProperty] private string _barTodayText = "—";
    [ObservableProperty] private string _barPendingText = "—";

    private async Task RefreshStatusBarAsync()
    {
        try
        {
            var lines = _monitor.Snapshot();
            BarCpuText = GetValue(lines, 0);
            BarMemText = GetValue(lines, 1);
            BarDiskText = GetValue(lines, 2);

            // 网络/心跳状态：TODO 待接入真实数据源后更新
            // NetworkHealth / HeartbeatHealth / BarNetStateText / BarHeartbeatText
        }
        catch { /* 忽略 */ }

        try
        {
            var stats = await _services.GetRequiredService<IUsbPortCardService>()
                                       .GetTodayStatsAsync();
            BarTodayText = stats.FileCount.ToString();
        }
        catch { BarTodayText = "—"; }

        try
        {
            var pending = await _uploadService.CountPendingUploadsAsync();
            BarPendingText = pending.ToString();
        }
        catch { BarPendingText = "—"; }
    }

    private static string GetValue(IReadOnlyList<MonitorLine> lines, int i)
        => lines.Count > i ? lines[i].Value : "—";

    /// <summary>启动时读一次：第一个非回环、已启用的 IPv4。</summary>
    private static string GetLocalIpv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var addr = ni.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (addr is not null) return addr.Address.ToString();
            }
        }
        catch { /* 忽略 */ }
        return "—";
    }

    // ===================== License（事件驱动） =====================
    [ObservableProperty] private string _licenseBadgeText = "—";
    [ObservableProperty] private IBrush _licenseBadgeBackground = Brushes.Transparent;
    [ObservableProperty] private IBrush _licenseBadgeForeground = Brushes.White;

    private void OnLicenseChanged(object? sender, LicenseCheckResult r)
        => Dispatcher.UIThread.Post(() => ApplyLicense(r));

    private async Task RefreshLicenseAsync()
    {
        try { ApplyLicense(await _licenseService.CheckAsync()); }
        catch { /* 忽略 */ }
    }

    private void ApplyLicense(LicenseCheckResult check)
    {
        (LicenseBadgeText, LicenseBadgeBackground) = check.Status switch
        {
            LicenseStatus.Activated =>
                ($"正式版·剩余{check.DaysLeft}天", new SolidColorBrush(Color.Parse("#14532d"))),
            LicenseStatus.Locked =>
                ("授权已到期", new SolidColorBrush(Color.Parse("#7f1d1d"))),
            _ =>
                ($"试用·剩余{check.DaysLeft}天", new SolidColorBrush(Color.Parse("#92400e")))
        };
        LicenseBadgeForeground = new SolidColorBrush(Color.Parse("#fef3c7"));
    }

    // ===================== 报警 =====================
    [ObservableProperty] private bool _isAlertBannerVisible;
    [ObservableProperty] private string _alertBannerText = "";

    [ObservableProperty] private bool _isAlertDrawerOpen;

    public string AlertDrawerTransform => IsAlertDrawerOpen
        ? "translateX(0px)"
        : "translateX(360px)";

    partial void OnIsAlertDrawerOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(AlertDrawerTransform));
    }

    public ObservableCollection<AlertDto> DrawerAlerts { get; } = new();

    /// <summary>由 View 层报警横幅点击调用。</summary>
    public async Task OpenAlertDrawerAsync()
    {
        IsAlertBannerVisible = false;

        try
        {
            var list = await _alertService.GetAlertsAsync(null, AlertStatus.Pending, 20);
            DrawerAlerts.Clear();
            foreach (var a in list) DrawerAlerts.Add(a);
        }
        catch { /* 忽略 */ }

        IsAlertDrawerOpen = true;
    }

    [RelayCommand] private void CloseAlertDrawer() => IsAlertDrawerOpen = false;

    [RelayCommand]
    private async Task OpenAlertsAsync()
    {
        IsAlertBannerVisible = false;
        IsAlertDrawerOpen = false;
        await NavigateAsync("alerts");
    }

    private async Task RefreshAlertBannerAsync()
    {
        try
        {
            var pending = await _alertService.CountPendingAsync();
            IsAlertBannerVisible = pending > 0;
            AlertBannerText = pending > 0
                ? $"⚠ 有 {pending} 条待处理报警（点击查看）"
                : string.Empty;

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
                    DesktopAlertSound.Play();
                    // 方案 B：仅提示，用户点 banner 才开抽屉
                    // 若要自动弹出抽屉：await OpenAlertDrawerAsync();
                }
            }
        }
        catch { /* 忽略 */ }
    }

    // ===================== Dispose =====================
    public void Dispose()
    {
        _clockTimer.Stop();
        _alertTimer.Stop();
        _statusBarTimer.Stop();

        _sessions.SessionChanged -= OnSessionChanged;
        _licenseService.LicenseChanged -= OnLicenseChanged;
        _idleTracker.WarningChanged -= OnIdleWarningChanged;

        _logsViewModel?.Dispose();
        _currentModuleDisposable?.Dispose();
    }
}
