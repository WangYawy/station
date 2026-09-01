using Microsoft.Extensions.Options;
using Station.Application.Authentication;
using Station.Application.Authorization;
using Station.Domain.Authorization;

namespace Station.Desktop.Application.OperationAccess;

public sealed class OperationAccessService : IOperationAccessService
{
    private readonly OperationAuthOptions _options;

    public OperationAccessService(IOptions<OperationAuthOptions> options)
    {
        _options = options.Value;
    }

    public bool IsLoginRequired(string moduleKey) =>
        _options.RequiredModules.Contains(moduleKey);

    public string? GetRequiredPermission(string moduleKey) => moduleKey switch
    {
        "collect" => PermissionCodes.FileManage,
        "history" => PermissionCodes.FileView,
        "logs" => PermissionCodes.AuditView,
        "alerts" => PermissionCodes.AlertView,
        "settings" => PermissionCodes.SettingView,
        _ => null
    };

    public bool HasPermission(AuthSession session, string moduleKey)
    {
        var permission = GetRequiredPermission(moduleKey);
        return permission is null
               || session.Roles.Contains(AuthRoleCodes.Admin)
               || session.Permissions.Contains(permission);
    }
}
