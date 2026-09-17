using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Station.Application.Alerts;
using Station.Application.Audit;
using Station.Application.Authentication;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Monitoring;
using Station.Application.OperationAccess;
using Station.Application.Recorders;
using Station.Application.Session;
using Station.Application.Settings;
using Station.Application.Uploading;
using Station.Application.UsbPortCard;
using Station.Application.UsbPortCard.Events;
using Station.Contracts;
using Station.Desktop.Services;
using Station.Desktop.Services.Kiosk;
using Station.Desktop.ViewModels;
using Station.Domain.Collecting;

namespace Station.Desktop.Views;
public partial class ShellWindow2 : Window
{
   


    private static readonly (string Key, string Title)[] ModuleCatalog =
    [
        ("workbench", "工作台"),
        //("collect", "采集作业"),
        ("data", "数据管理"),
        ("logs", "日志中心"),
        //("alerts", "报警中心"),
        ("settings", "设置")
    ];

    private readonly ISessionManager _sessions;
    private readonly IOperationAccessService _operationAccess;
    private readonly IAlertService _alertService;
    private readonly ILicenseService _licenseService;
    private readonly IUploadService _uploadService;
    private readonly ICollectTaskService _collectService;
    private readonly WindowModeOptions _windowMode;

    private readonly SessionIdleTracker _idleTracker;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _alertTimer;
    private readonly DispatcherTimer _statusBarTimer;
    private readonly SystemMonitorService _monitor;

    private int _clockTicks;
    private long? _lastAlertId;
    private readonly Dictionary<string, Button> _navButtons;
    private IDisposable? _currentViewModel;

    public ShellWindow2()
    {
        InitializeComponent();

        var services = App.Services!;
        _sessions = services.GetRequiredService<ISessionManager>();
        _operationAccess = services.GetRequiredService<IOperationAccessService>();
        _alertService = services.GetRequiredService<IAlertService>();
        _licenseService = services.GetRequiredService<ILicenseService>();
        _uploadService = services.GetRequiredService<IUploadService>();
        _collectService = services.GetRequiredService<ICollectTaskService>();
        _windowMode = services.GetRequiredService<IOptions<WindowModeOptions>>().Value;

        _sessions.SessionChanged += OnSessionChanged;

        _navButtons = new Dictionary<string, Button>
        {
            ["workbench"] = NavWorkbench,
            // ["collect"] = NavCollect,
            ["data"] = NavData,
            ["logs"] = NavLogs,
            ["alerts"] = NavAlerts,
            ["settings"] = NavSettings
        };

        ApplyWindowMode(); // 应用 Kiosk 窗口模式

        _idleTracker = new SessionIdleTracker(
            _sessions,
            services.GetRequiredService<AuthOptions>());
        _idleTracker.WarningChanged += OnIdleWarningChanged;
        _idleTracker.Attach(this);

        // 时钟
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += async (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (++_clockTicks % (60 * 5) == 0)
            {
                await RefreshLicenseAsync();
            }
        };
        _clockTimer.Start();

        // 报警轮询（5秒）
        _alertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _alertTimer.Tick += async (_, _) => await RefreshAlertBannerAsync();
        _alertTimer.Start();

        // 底栏监控（5 秒）
        _monitor = new SystemMonitorService(services.GetRequiredService<CollectOptions>());
        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarTimer.Tick += async (_, _) => await RefreshStatusBarAsync();
        _statusBarTimer.Start();

        OnSessionChanged(); // 登录session监控
        _ = NavigateAsync("workbench");
        _ = RefreshLicenseAsync();
        _ = RefreshStatusBarAsync();
    }

    #region Kiosk 窗口模式
    private readonly IKioskGuard _kiosk = KioskGuardFactory.Create();

    /// <summary>当前打开的模态对话框数量（>0 时跳过失焦拉回，避免抢走对话框焦点）</summary>
    private int _modalDepth;

    private void ApplyWindowMode()
    {
        if (_windowMode.Kiosk)
        {
            WindowDecorations = WindowDecorations.None;
            WindowState = WindowState.FullScreen;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_windowMode.LockShortcuts)
        {
            _kiosk.Install();
            _kiosk.RequestExit += ForceExit;

            // 软件层兜底：失焦立即拉回（跨平台一致行为）
            Deactivated += OnShellDeactivated;

            // 应用层拦截关键快捷键
            AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        }

        // 无论是否 Kiosk 模式，都跟踪 Shift 状态（后门依赖它）
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// 外壳已停用
    /// </summary>
    private void OnShellDeactivated(object? sender, EventArgs e)
    {
        _shiftPressed = false; // 如果窗口失焦再回来，_shiftPressed 可能停在错误状态，重置为 false 兜底

        // 有模态对话框打开时，不抢回焦点（否则对话框无法输入）
        if (_modalDepth > 0) return;

        if (!_windowMode.Kiosk || !_windowMode.LockShortcuts) return;
        // 若已经有子窗口（如对话框）是活动的，不抢回，这样即使某处忘了用 ShowModalAsync 包裹，只要对话框还在前台，也不会被抢走焦点。两重保险
        var hasActiveChild = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                             desktop.Windows.Any(w => w != this && w.IsActive && w.IsVisible);

        if (hasActiveChild == true) return;

        // 拉回并置顶
        Dispatcher.UIThread.Post(() =>
        {
            WindowState = WindowState.FullScreen;
            Topmost = true;
            Activate();
        }, DispatcherPriority.Send);
    }

    /// <summary>
    /// 屏蔽键盘 Esc  / Alt+F4 / Ctrl+W 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_windowMode.LockShortcuts) return;

        // 屏蔽应用内 Esc / Alt+F4 / Ctrl+W / Alt+Tab（如果钩子未生效）
        if (e.Key == Key.Escape) { e.Handled = true; return; }
        if (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; return; }
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; return; }
        if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; return; }
    }

    // 监控shift键按下
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            _shiftPressed = true;
        }
    }
    // 监控shift键弹起
    private void OnGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            _shiftPressed = false;
        }
    }

    /// <summary>
    /// 以模态方式显示对话框，期间暂停"失焦拉回"逻辑。
    /// </summary>
    private async Task<TResult> ShowModalAsync<TResult>(
        Window dialog,
        Func<Task<TResult>> showAction)
    {
        _modalDepth++;
        try
        {
            return await showAction();
        }
        finally
        {
            _modalDepth--;

            // 对话框关闭后，若仍在 Kiosk 模式，主动拉回主窗口焦点
            if (_modalDepth == 0 && _windowMode.Kiosk && _windowMode.LockShortcuts)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WindowState = WindowState.FullScreen;
                    Topmost = true;
                    Activate();
                }, DispatcherPriority.Background);
            }
        }
    }
    #endregion

    #region // 退出程序（顶栏关闭按钮）
    // 用于跟踪 Shift 键当前是否按住（供"退出"按钮读取）
    private bool _shiftPressed;
    private CancellationTokenSource? _longPressCts;
    private bool _longPressTriggered;     // 标记长按是否已触发（避免松手重复处理）

    /// <summary>退出按钮按下</summary>
    private void OnCloseAppPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_windowMode.ShiftEnabled)
        {
            e.Handled = true;
            return;
        }

        _longPressTriggered = false;

        _longPressCts = new CancellationTokenSource();
        var token = _longPressCts.Token;
        var threshold = TimeSpan.FromSeconds(_windowMode.LongPressSeconds);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(threshold, token);
                if (token.IsCancellationRequested) return;

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    _longPressTriggered = true;
                    await OnLongPressCompletedAsync();
                });
            }
            catch (TaskCanceledException) { /* 用户提前松手 */ }
        }, token);

        // e.Handled = true; // 鼠标单击事件会失效
    }

    /// <summary>退出按钮按下释放</summary>
    private void OnCloseAppPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CancelLongPress();

        //// 长按已触发 → PIN 流程已接管，不要再走常规流程
        //if (_longPressTriggered)
        //{
        //    _longPressTriggered = false;
        //    e.Handled = true;
        //    return;
        //}

        //// 未满长按时长 → 走常规退出流程
        //await RunNormalExitFlowAsync();
        // e.Handled = true;
    }
    /// <summary>退出按钮按下丢失</summary>
    private void OnCloseAppPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_longPressTriggered) return;
        CancelLongPress();
        //_longPressTriggered = false;
    }
    private async void OnCloseAppClick(object? sender, RoutedEventArgs e)
    {
        // 长按已处理 → 消费标志位并忽略本次 Click
        if (_longPressTriggered)
        {
            _longPressTriggered = false;
            return;
        }

        // 常规退出流程（Shift 后门 / 确认对话框）
        await RunNormalExitFlowAsync();
    }

    private void CancelLongPress()
    {
        _longPressCts?.Cancel();
        _longPressCts?.Dispose();
        _longPressCts = null;
    }

    /// <summary> 长按3秒 </summary>
    private async Task OnLongPressCompletedAsync()
    {
        // 配置里没设 PIN → 退化为直接退出（相当于"长按即退"）
        if (string.IsNullOrEmpty(_windowMode.ExitPin))
        {
            ForceExit();
            return;
        }

        var vm = new PinViewModel(
            _windowMode.ExitPin,
            _windowMode.ExitPinLength);

        var dialog = new PinDialog(vm);

        // 复用模态保护：期间不抢回焦点
        var ok = await ShowModalAsync(dialog, () => dialog.ShowAsync(this));

        if (ok) ForceExit();
    }

    /// <summary>
    /// 常规退出流程：Shift 后门 → 确认对话框 → 退出
    /// </summary>
    private async Task RunNormalExitFlowAsync()
    {
        // 通道 1：Shift + 点击（键盘设备）
        if (_windowMode.ShiftEnabled && _shiftPressed)
        {
            ForceExit();
            return;
        }

        if (_windowMode.ConfirmOnExit)
        {
            var ok = await ConfirmExitAsync();
            if (!ok) return;
        }

        ForceExit();
    }
    private async Task<bool> ConfirmExitAsync()
    {
        // 已登录：直接弹确认框
        if (_sessions.IsAuthenticated)
        {
            var vm = new ExitConfirmViewModel(_sessions);
            var dialog = new ExitConfirmDialog(vm);
            return await ShowModalAsync(dialog, () => dialog.ShowAsync(this));
        }

        // 未登录：先走登录流程
        var loggedIn = await ShowLoginAsync(presetUserName: _windowMode.DefaultAdminUsername);
        if (!loggedIn)
        {
            return false;   // 用户取消或登录失败
        }

        // 登录成功 → 检查角色
        var roles = _sessions.Current?.Roles;
        var isAdmin = roles is { Count: > 0 } &&
                      roles.Any(r => string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && _windowMode.RequireAdminOnExitWhenAnonymous)
        {
            // 非管理员：清除刚建立的会话 + 提示 + 拒绝退出
            _sessions.Clear();
            await ShowNotAdminHintAsync();
            return false;
        }

        // 管理员登录成功 → 允许退出
        return true;
    }
    private async Task ShowNotAdminHintAsync()
    {
        var dlg = new Window
        {
            Title = "权限不足",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Background = (Avalonia.Media.IBrush?)Avalonia.Application.Current?.FindResource("SurfaceBackground"),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 20,
                Children =
            {
                new TextBlock
                {
                    Text = "该账号不是管理员，无权退出程序。",
                    FontSize = 14,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                },
                new Button
                {
                    Content = "知道了",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    MinWidth = 100
                }
            }
            }
        };

        if (dlg.Content is StackPanel sp && sp.Children[^1] is Button btn)
            btn.Click += (_, _) => dlg.Close();

        await ShowModalAsync(dlg, async () =>
        {
            await dlg.ShowDialog(this);
            return true;
        });
    }

    private void ForceExit()
    {
        _kiosk.Dispose();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
            life.Shutdown();
        else
            Close();
    }
    #endregion

    #region // 底部状态栏刷新
    private async Task RefreshStatusBarAsync()
    {
        try
        {
            var lines = _monitor.Snapshot();
            BarCpuText.Text = $"{GetValue(lines, 0)}";
            BarMemText.Text = $"{GetValue(lines, 1)}";
            BarDiskText.Text = $"{GetValue(lines, 2)}";
            //BarNetText.Text = $"{GetValue(lines, 3)}";
        }
        catch { /* 监控异常忽略 */ }

        try
        {
            var stats = await App.Services!
                .GetRequiredService<IUsbPortCardService>()
                .GetTodayStatsAsync();
            BarTodayText.Text = $"{stats.FileCount}";
        }
        catch { BarTodayText.Text = "—"; }

        try
        {
            var pending = await _uploadService.CountPendingUploadsAsync();
            BarPendingText.Text = $"{pending}";
        }
        catch { BarPendingText.Text = "—"; }
    }

    private static string GetValue(IReadOnlyList<MonitorLine> lines, int index) =>
        lines.Count > index ? lines[index].Value : "—";
    #endregion


    #region // 顶部主题按钮 → 切换明/暗
    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
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
        ThemeToggleIcon.Data = (StreamGeometry)Avalonia.Application.Current!.FindResource(isDark ? "IconSun" : "IconMoon")!;
    }
    #endregion


    #region// 导航
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
                 _collectService,
                 _operationAccess,
                 services.GetRequiredService<IUsbPortCardService>(),
                 services.GetRequiredService<IUsbPortCardEventService>(),
                 services.GetRequiredService<WindowModeOptions>());

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

        if (moduleKey == "data" && overrideTitle is null)
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
                //services.GetRequiredService<INetworkSettingsService>(),
                services.GetRequiredService<ISystemSelfCheckService>(),
                services.GetRequiredService<ILicenseService>(),
                _sessions,
                _operationAccess);
            ModuleContent.Content = new SettingsModuleView { DataContext = viewModel };
            _currentViewModel = viewModel;
            return;
        }

        var title = overrideTitle ?? ModuleCatalog.First(m => m.Key == moduleKey).Title;
        var message = overrideMessage ?? "该模块正在开发中";
        ModuleContent.Content = new ModulePlaceholderView
        {
            DataContext = new ModulePlaceholderViewModel(title, message)
        };
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

    private void UpdateNavSelection(string moduleKey)
    {
        foreach (var (key, button) in _navButtons)
        {
            button.Classes.Set("nav-pill-selected", key == moduleKey);
        }
    }
    #endregion

    #region // 登录状态
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

    private async Task<bool> ShowLoginAsync(string? presetUserName = null)
    {
        var services = App.Services!;
        var viewModel = new LoginViewModel(
            services.GetRequiredService<IAuthenticationService>(),
            _sessions);

        if (!string.IsNullOrEmpty(presetUserName))
        {
            viewModel.UserName = presetUserName;
        }

        var dialog = new LoginDialog(viewModel, this);
        return await ShowModalAsync(dialog, () => dialog.ShowDialog<bool>(this));
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
    #endregion

    #region // 报警监控
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
    #endregion

    // 公告
    private async void OnAlertBannerTapped(object? sender, TappedEventArgs e)
    {
        AlertBanner.IsVisible = false;
        await NavigateAsync("alerts");
    }

    // 授权监控
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
