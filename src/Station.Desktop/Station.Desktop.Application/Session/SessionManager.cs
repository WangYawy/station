using Station.Application.Authentication;
using Station.Application.Authorization;

namespace Station.Desktop.Application.Session;

public sealed class SessionManager : ISessionManager
{
    public AuthSession? Current { get; private set; }

    public bool IsAuthenticated => Current is not null;

    public event Action? SessionChanged;

    public event Action? SessionExpired;

    public void Start(AuthSession session)
    {
        Current = session;
        SessionChanged?.Invoke();
    }

    public void Clear()
    {
        if (Current is null)
        {
            return;
        }

        Current = null;
        SessionExpired?.Invoke();
        SessionChanged?.Invoke();
    }

    public bool HasPermission(string permissionCode) =>
        Current is { } session &&
        (session.Roles.Contains(AuthRoleCodes.Admin) || session.Permissions.Contains(permissionCode));
}
