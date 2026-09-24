using Station.Domain.Entities;

namespace Station.Infrastructure.Persistence;

public static class AuthSeedData
{
    // 移除所有角色常量、权限目录、种子角色列表，仅保留数据库实体类型
    public static readonly Type[] EntityTypes =
    [
        typeof(Account),
        typeof(User),
        typeof(Dept),
        typeof(Role),
        typeof(Permission),
        typeof(RolePermission),
        typeof(UserRole),
        typeof(AuditLog),
        typeof(AuditLogArchive)
    ];
}
