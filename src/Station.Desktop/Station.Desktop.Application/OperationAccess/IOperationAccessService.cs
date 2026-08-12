using Station.Application.Authentication;

namespace Station.Desktop.Application.OperationAccess;

/// <summary>
/// 操作访问控制：模块是否需要登录、登录后是否需要对应权限点。
/// </summary>
public interface IOperationAccessService
{
    bool IsLoginRequired(string moduleKey);

    string? GetRequiredPermission(string moduleKey);

    bool HasPermission(AuthSession session, string moduleKey);
}
