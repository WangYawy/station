using Station.Application.Authentication;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;

namespace Station.Application.Authorization;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<User> _users;
    private readonly IRepository<UserRole> _userRoles;
    private readonly IRepository<Role> _roles;
    private readonly IRepository<RolePermission> _rolePermissions;
    private readonly IRepository<Permission> _permissions;

    public AuthorizationService(
        IRepository<Account> accounts,
        IRepository<User> users,
        IRepository<UserRole> userRoles,
        IRepository<Role> roles,
        IRepository<RolePermission> rolePermissions,
        IRepository<Permission> permissions)
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
            var userRoles = await _userRoles.GetListAsync(ur => ur.UserId == user.Id);
            var roleIds = userRoles.Select(x => x.RoleId).Distinct().ToList();
            if (roleIds.Count > 0)
            {
                var roles = await _roles.GetListAsync(r => roleIds.Contains(r.Id) && r.IsActive);
                if (roles.Count > 0)
                {
                    roleCodes = roles.Select(r => r.Code).ToList();
                    dataScope = (DataScope)roles.Min(r => (int)r.DataScope);

                    var links = await _rolePermissions.GetListAsync(rp => roleIds.Contains(rp.RoleId));
                    var permissionIds = links.Select(x => x.PermissionId).Distinct().ToList();
                    if (permissionIds.Count > 0)
                    {
                        var permissions = await _permissions.GetListAsync(p => permissionIds.Contains(p.Id) && p.IsActive);
                        permissionCodes = permissions.Select(p => p.Code).ToList();
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
        return session.Roles.Contains(AuthRoleCodes.Admin) || session.Permissions.Contains(permissionCode);
    }
}

/// <summary>预置角色编码常量。</summary>
public static class AuthRoleCodes
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Operator = "operator";
    public const string Auditor = "auditor";
}
