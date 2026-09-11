using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;

namespace Station.Application.Authorization;

public sealed class DataScopeProvider : IDataScopeProvider
{
    private readonly IRepository<User> _users;
    private readonly IRepository<Dept> _depts;
    private readonly IRepository<UserRole> _userRoles;
    private readonly IRepository<Role> _roles;

    public DataScopeProvider(
        IRepository<User> users,
        IRepository<Dept> depts,
        IRepository<UserRole> userRoles,
        IRepository<Role> roles)
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

        var userRoles = await _userRoles.GetListAsync(ur => ur.UserId == user.Id);
        var roleIds = userRoles.Select(x => x.RoleId).Distinct().ToList();
        var scope = DataScope.Self;
        if (roleIds.Count > 0)
        {
            var roles = await _roles.GetListAsync(r => roleIds.Contains(r.Id) && r.IsActive);
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
        var children = await _depts.GetListAsync(d => d.ParentId == parentId && d.IsActive);
        foreach (var child in children)
        {
            if (deptIds.Add(child.Id))
            {
                await AddDescendantsAsync(child.Id, deptIds);
            }
        }
    }
}
