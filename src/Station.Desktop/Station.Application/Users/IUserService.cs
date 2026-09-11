namespace Station.Application.Users;

/// <summary>用户/部门/角色/权限管理服务（RBAC 维护侧）。</summary>
public interface IUserService
{
    // 部门
    Task<IReadOnlyList<DeptDto>> GetDeptTreeAsync();

    Task<long> CreateDeptAsync(DeptDto dto);

    Task<OpResult> UpdateDeptAsync(DeptDto dto);

    Task<OpResult> DeleteDeptAsync(long id);

    // 用户
    Task<IReadOnlyList<UserDto>> GetUsersAsync();

    Task<OpResult> CreateUserAsync(UserDto dto, string? userName = null, string? password = null);

    Task<OpResult> UpdateUserAsync(UserDto dto);

    Task<OpResult> DeleteUserAsync(long id);

    Task<OpResult> AssignRolesAsync(long userId, IReadOnlyList<long> roleIds);

    // 账号
    Task<CreateAccountResult> CreateAccountAsync(long userId, string userName, string password);

    Task<OpResult> ResetPasswordAsync(long accountId, string newPassword);

    // 角色与权限
    Task<IReadOnlyList<RoleDto>> GetRolesAsync();

    Task<long> CreateRoleAsync(RoleDto dto);

    Task<OpResult> UpdateRoleAsync(RoleDto dto);

    Task<OpResult> DeleteRoleAsync(long id);

    Task<OpResult> SetRolePermissionsAsync(long roleId, IReadOnlyList<long> permissionIds);

    Task<IReadOnlyList<PermissionDto>> GetPermissionCatalogAsync();
}
