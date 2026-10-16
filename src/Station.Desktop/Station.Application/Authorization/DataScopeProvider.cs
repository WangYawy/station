using Station.Domain.Departments;
using Station.Domain.Roles;
using Station.Domain.Users;

namespace Station.Application.Authorization;

public sealed class DataScopeProvider : IDataScopeProvider
{
    private readonly IUserRepository _users;
    private readonly IDeptRepository _depts;
    private readonly IUserRoleRepository _userRoles;
    private readonly IRoleRepository _roles;

    public DataScopeProvider(
        IUserRepository users,
        IDeptRepository depts,
        IUserRoleRepository userRoles,
        IRoleRepository roles)
    {
        _users = users;
        _depts = depts;
        _userRoles = userRoles;
        _roles = roles;
    }

    public async Task<DataScopeResult> GetDataScopeAsync(long userId)
    {
        var user = await _users.GetByIdAsync(userId)
                   ?? throw new InvalidOperationException($"用户 {userId} 不存在");

        var roleIds = await _userRoles.GetRoleIdsByUserIdAsync(user.Id);
        var scope = DataScope.Self;

        if (roleIds.Count > 0)
        {
            var roles = await _roles.GetActiveByIdsAsync(roleIds);
            if (roles.Count > 0)
            {
                scope = (DataScope)roles.Min(r => (int)r.DataScope);
            }
        }

        if (scope == DataScope.All)
        {
            return new DataScopeResult(scope, true, [], user.DeptId);
        }

        var deptIds = new HashSet<long> { user.DeptId };
        if (scope == DataScope.DeptAndChildren)
        {
            await AddDescendantsAsync(user.DeptId, deptIds);
        }

        return new DataScopeResult(scope, false, deptIds, user.DeptId);
    }

    private async Task AddDescendantsAsync(long parentId, HashSet<long> deptIds)
    {
        var children = await _depts.GetActiveChildrenAsync(parentId);
        foreach (var child in children)
        {
            if (deptIds.Add(child.Id))
            {
                await AddDescendantsAsync(child.Id, deptIds);
            }
        }
    }
}
