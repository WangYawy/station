using Station.Application.Authentication;

namespace Station.Application.Authorization;

/// <summary>授权服务：解析账号会话（角色/权限/数据范围）与权限判定。</summary>
public interface IAuthorizationService
{
    Task<AuthSession> GetSessionAsync(long accountId);

    Task<bool> HasPermissionAsync(long accountId, string permissionCode);
}
