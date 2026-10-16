using Station.Infrastructure.Persistence.Accounts;
using Station.Infrastructure.Persistence.Auditing;
using Station.Infrastructure.Persistence.Departments;
using Station.Infrastructure.Persistence.Permissions;
using Station.Infrastructure.Persistence.Roles;
using Station.Infrastructure.Persistence.Users;

namespace Station.Infrastructure.Persistence;

public static class AuthSeedData
{
    // 移除所有角色常量、权限目录、种子角色列表，仅保留数据库实体类型
    public static readonly Type[] EntityTypes =
    [
        typeof(AccountPo),
        typeof(UserPo),
        typeof(DeptPo),
        typeof(RolePo),
        typeof(PermissionPo),
        typeof(RolePermissionPo),
        typeof(UserRolePo),
        typeof(AuditLogArchivePo)
    ];
}
