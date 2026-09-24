using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Authentication;
using Station.Application.Session;
using Station.Application.Settings;
using Station.Desktop.Services.Kiosk;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class ShellWindow : Window
{
    private readonly ISessionManager _sessions;
    private readonly WindowModeOptions _windowMode;
    private readonly ShellViewModel _viewModel;

    public ShellWindow()
    {
        InitializeComponent();

        var services = App.Services!;
        _sessions = services.GetRequiredService<ISessionManager>();
        _windowMode = services.GetRequiredService<WindowModeOptions>();

        _viewModel = new ShellViewModel(services);
        _viewModel.RegisterLoginRequest(() => ShowLoginAsync());
        DataContext = _viewModel;

        ApplyWindowMode();                    // Kiosk 窗口模式
        _viewModel.AttachIdleTracker(this);   // 会话空闲追踪挂到 Window 上
    }

    #region Kiosk 窗口模式
    private readonly IKioskGuard _kiosk = KioskGuardFactory.Create();

    /// <summary>当前打开的模态对话框数量（>0 时跳过失焦拉回）。</summary>
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

            Deactivated += OnShellDeactivated;
            AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
        }

        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnGlobalKeyUp, RoutingStrategies.Tunnel);
    }

    private void OnShellDeactivated(object? sender, EventArgs e)
    {
        _shiftPressed = false;

        if (_modalDepth > 0) return;
        if (!_windowMode.Kiosk || !_windowMode.LockShortcuts) return;

        var hasActiveChild =
            Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.Windows.Any(w => w != this && w.IsActive && w.IsVisible);

        if (hasActiveChild) return;

        Dispatcher.UIThread.Post(() =>
        {
            WindowState = WindowState.FullScreen;
            Topmost = true;
            Activate();
        }, DispatcherPriority.Send);
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_windowMode.LockShortcuts) return;

        // 报警抽屉打开时，ESC 关闭抽屉而不是被屏蔽
        if (e.Key == Key.Escape && _viewModel.IsAlertDrawerOpen)
        {
            _viewModel.CloseAlertDrawerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape) { e.Handled = true; return; }
        if (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; return; }
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; return; }
        if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = true; return; }
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
            _shiftPressed = true;
    }

    private void OnGlobalKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
            _shiftPressed = false;
    }

    /// <summary>以模态方式显示对话框，期间暂停"失焦拉回"逻辑。</summary>
    internal async Task<TResult> ShowModalAsync<TResult>(
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

    #region 退出程序（顶栏关闭按钮）
    private bool _shiftPressed;
    private CancellationTokenSource? _longPressCts;
    private bool _longPressTriggered;

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
    }

    private void OnCloseAppPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CancelLongPress();
    }

    private void OnCloseAppPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_longPressTriggered) return;
        CancelLongPress();
    }

    private async void OnCloseAppClick(object? sender, RoutedEventArgs e)
    {
        if (_longPressTriggered)
        {
            _longPressTriggered = false;
            return;
        }

        await RunNormalExitFlowAsync();
    }

    private void CancelLongPress()
    {
        _longPressCts?.Cancel();
        _longPressCts?.Dispose();
        _longPressCts = null;
    }

    private async Task OnLongPressCompletedAsync()
    {
        if (string.IsNullOrEmpty(_windowMode.ExitPin))
        {
            ForceExit();
            return;
        }

        var vm = new PinViewModel(_windowMode.ExitPin, _windowMode.ExitPinLength);
        var dialog = new PinDialog(vm);

        var ok = await ShowModalAsync(dialog, () => dialog.ShowAsync(this));
        if (ok) ForceExit();
    }

    private async Task RunNormalExitFlowAsync()
    {
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
        if (_sessions.IsAuthenticated)
        {
            var vm = new ExitConfirmViewModel(_sessions);
            var dialog = new ExitConfirmDialog(vm);
            return await ShowModalAsync(dialog, () => dialog.ShowAsync(this));
        }

        var loggedIn = await ShowLoginAsync(presetUserName: _windowMode.DefaultAdminUsername);
        if (!loggedIn) return false;

        var roles = _sessions.Current?.Roles;
        var isAdmin = roles is { Count: > 0 } &&
                      roles.Any(r => string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase));

        if (!isAdmin && _windowMode.RequireAdminOnExitWhenAnonymous)
        {
            _sessions.Clear();
            await ShowNotAdminHintAsync();
            return false;
        }

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
            Background = (IBrush?)Avalonia.Application.Current?.FindResource("SurfaceBackground"),
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = "该账号不是管理员，无权退出程序。",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
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

    #region 登录 / 登出
    /// <summary>弹出登录对话框；登录成功后回到当前页面。</summary>
    private async Task<bool> ShowLoginAsync(string? presetUserName = null)
    {
        var services = App.Services!;
        var viewModel = new LoginViewModel(
            services.GetRequiredService<IAuthenticationService>(),
            _sessions);

        if (!string.IsNullOrEmpty(presetUserName))
            viewModel.UserName = presetUserName;

        var dialog = new LoginDialog(viewModel, this);
        return await ShowModalAsync(dialog, () => dialog.ShowDialog<bool>(this));
    }
    #endregion

    #region 顶栏交互
    /// <summary>session-badge 点击：未登录 → 弹登录；已登录 → 确认 → 登出。</summary>
    private async void OnSessionBadgeTapped(object? sender, TappedEventArgs e)
    {
        if (!_sessions.IsAuthenticated)
        {
            await _viewModel.RaiseLoginRequestAsync();
            return;
        }

        var ok = await ConfirmLogoutAsync();
        if (!ok) return;

        await _viewModel.LogoutAsync();
    }

    private Task<bool> ConfirmLogoutAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        var dlg = new Window
        {
            Title = "退出登录",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Colors.White),
        };

        var ok = new Button { Content = "确认退出", MinWidth = 100 };
        var cancel = new Button { Content = "取消", MinWidth = 100 };

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = "确认退出当前登录账号？",
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Children = { ok, cancel }
                }
            }
        };

        ok.Click += (_, _) => { tcs.TrySetResult(true); dlg.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(false); dlg.Close(); };
        dlg.Closed += (_, _) => tcs.TrySetResult(false);

        _ = ShowModalAsync(dlg, async () =>
        {
            await dlg.ShowDialog(this);
            return true;
        });

        return tcs.Task;
    }

    /// <summary>报警横幅点击：打开右侧抽屉。</summary>
    private async void OnAlertBannerTapped(object? sender, TappedEventArgs e)
    {
        await _viewModel.OpenAlertDrawerAsync();
    }

    /// <summary>遮罩点击：关闭抽屉。</summary>
    private void OnAlertDrawerOverlayTapped(object? sender, TappedEventArgs e)
    {
        _viewModel.CloseAlertDrawerCommand.Execute(null);
    }
    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
