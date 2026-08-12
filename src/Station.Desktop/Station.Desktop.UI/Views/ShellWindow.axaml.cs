using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Authentication;
using Station.Application.Collecting;
using Station.Application.Uploading;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Session;
using Station.Desktop.UI.Services;
using Station.Desktop.UI.ViewModels;

namespace Station.Desktop.UI.Views;

public partial class ShellWindow : Window
{
    private static readonly (string Key, string Title)[] ModuleCatalog =
    [
        ("workbench", "工作台"),
        ("collect", "采集作业"),
        ("history", "历史记录"),
        ("logs", "日志中心"),
        ("settings", "设置")
    ];

    private readonly ISessionManager _sessions;
    private readonly IOperationAccessService _operationAccess;
    private readonly SessionIdleTracker _idleTracker;
    private readonly DispatcherTimer _clockTimer;
    private readonly Dictionary<string, Button> _navButtons;
    private IDisposable? _currentViewModel;

    public ShellWindow()
    {
        InitializeComponent();

        var services = App.Services!;
        _sessions = services.GetRequiredService<ISessionManager>();
        _operationAccess = services.GetRequiredService<IOperationAccessService>();
        _sessions.SessionChanged += OnSessionChanged;

        _navButtons = new Dictionary<string, Button>
        {
            ["workbench"] = NavWorkbench,
            ["collect"] = NavCollect,
            ["history"] = NavHistory,
            ["logs"] = NavLogs,
            ["settings"] = NavSettings
        };

        _idleTracker = new SessionIdleTracker(
            _sessions,
            services.GetRequiredService<AuthOptions>());
        _idleTracker.WarningChanged += OnIdleWarningChanged;
        _idleTracker.Attach(this);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clockTimer.Start();

        OnSessionChanged();
        _ = NavigateAsync("workbench");
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
                services.GetRequiredService<IAuthenticationService>(),
                services.GetRequiredService<ICollectTaskService>(),
                services.GetRequiredService<IUploadService>());
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
                _sessions);
            ModuleContent.Content = new CollectModuleView { DataContext = viewModel };
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
}
