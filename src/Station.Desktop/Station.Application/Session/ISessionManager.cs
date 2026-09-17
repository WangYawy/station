using Station.Application.Authentication;

namespace Station.Application.Session;

/// <summary>
/// 桌面端会话管理：保存当前登录会话，驱动 UI 在登录页/主界面间切换，
/// 并提供基于会话的权限判定（导航显隐、按钮可用性）。
/// </summary>
public interface ISessionManager
{
    AuthSession? Current { get; }

    bool IsAuthenticated { get; }

    event Action? SessionChanged;

    event Action? SessionExpired;

    void Start(AuthSession session);

    void Clear();

    bool HasPermission(string permissionCode);
}
