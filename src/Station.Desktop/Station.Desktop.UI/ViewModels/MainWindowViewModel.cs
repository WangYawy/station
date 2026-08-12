using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Authentication;
using Station.Desktop.Application.Session;
using Station.Infrastructure.Persistence;

namespace Station.Desktop.UI.ViewModels;

/// <summary>导航项：PermissionCode 为空表示登录后始终可见。</summary>
public sealed record NavItem(string Key, string Title, string Icon, string? PermissionCode);

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly NavItem[] NavCatalog =
    [
        new("dashboard", "工作台", "🏠", null),
        new("files", "文件台账", "📁", PermissionCodes.FileView),
        new("alerts", "报警中心", "🚨", PermissionCodes.AlertView),
        new("audit", "审计日志", "📜", PermissionCodes.AuditView),
        new("users", "用户管理", "👤", PermissionCodes.UserView),
        new("depts", "部门管理", "🏢", PermissionCodes.DeptView),
        new("roles", "角色权限", "🔑", PermissionCodes.RoleView),
        new("settings", "系统设置", "⚙️", PermissionCodes.SettingView)
    ];

    private readonly ISessionManager _sessions;
    private readonly IAuthenticationService _authentication;

    [ObservableProperty]
    private IReadOnlyList<NavItem> _navItems;

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private string _currentUserText = "";

    [ObservableProperty]
    private string _pageTitle = "工作台";

    public MainWindowViewModel(ISessionManager sessions, IAuthenticationService authentication)
    {
        _sessions = sessions;
        _authentication = authentication;

        NavItems = NavCatalog
            .Where(n => n.PermissionCode is null || sessions.HasPermission(n.PermissionCode))
            .ToList();
        SelectedNavItem = NavItems.FirstOrDefault();
        PageTitle = SelectedNavItem?.Title ?? "工作台";

        CurrentUserText = sessions.Current is { } session
            ? $"{session.Name ?? session.UserName}（{session.UserName}）"
            : string.Empty;
    }

    partial void OnSelectedNavItemChanged(NavItem? value) => PageTitle = value?.Title ?? "工作台";

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
