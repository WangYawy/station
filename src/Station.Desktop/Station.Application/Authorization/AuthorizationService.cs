using Station.Application.Authentication;
using Station.Domain.Accounts;
using Station.Domain.Enums;
using Station.Domain.Permissions;
using Station.Domain.Roles;
using Station.Domain.Users;

namespace Station.Application.Authorization;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly IAccountRepository _accounts;
    private readonly IUserRepository _users;
    private readonly IUserRoleRepository _userRoles;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IPermissionRepository _permissions;

    public AuthorizationService(
        IAccountRepository accounts,
        IUserRepository users,
        IUserRoleRepository userRoles,
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IPermissionRepository permissions)
    {
        _accounts = accounts;
        _users = users;
        _userRoles = userRoles;
        _roles = roles;
        _rolePermissions = rolePermissions;
        _permissions = permissions;
    }

    public async Task<AuthSession> GetSessionAsync(long accountId)
    {
        var account = await _accounts.GetByIdAsync(accountId)
                      ?? throw new InvalidOperationException($"账号 {accountId} 不存在");

        User? user = null;
        if (account.UserId is { } userId)
        {
            user = await _users.GetByIdAsync(userId);
        }

        var roleCodes = new List<string>();
        var permissionCodes = new List<string>();
        var dataScope = DataScope.Self;

        if (user is not null)
        {
            var roleIds = await _userRoles.GetRoleIdsByUserIdAsync(user.Id);
            if (roleIds.Count > 0)
            {
                var roles = await _roles.GetActiveByIdsAsync(roleIds);
                if (roles.Count > 0)
                {
                    roleCodes = roles.Select(r => r.Code).ToList();
                    dataScope = (DataScope)roles.Min(r => (int)r.DataScope);

                    var permissionIds = await _rolePermissions.GetPermissionIdsByRoleIdsAsync(roleIds);
                    if (permissionIds.Count > 0)
                    {
                        var permissions = await _permissions.GetByIdsAsync(permissionIds);
                        permissionCodes = permissions
                            .Where(p => p.IsActive)
                            .Select(p => p.Code)
                            .ToList();
                    }
                }
            }
        }

        return new AuthSession(
            account.Id,
            user?.Id,
            account.UserName,
            user?.UserNo,
            user?.Name,
            user?.DeptId,
            dataScope,
            roleCodes,
            permissionCodes);
    }

    public async Task<bool> HasPermissionAsync(long accountId, string permissionCode)
    {
        var session = await GetSessionAsync(accountId);
        return session.Roles.Contains(AuthRoleCodes.Admin)
            || session.Permissions.Contains(permissionCode);
    }
}

public static class AuthRoleCodes
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Operator = "operator";
    public const string Auditor = "auditor";
}
