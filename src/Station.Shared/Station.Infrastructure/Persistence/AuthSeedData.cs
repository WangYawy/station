using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Repositories;

namespace Station.Infrastructure.Persistence;

/// <summary>预置权限点目录（module:action）。</summary>
public static class PermissionCodes
{
    public const string UserView = "user:view";
    public const string UserManage = "user:manage";
    public const string UserAssignRole = "user:assign-role";
    public const string DeptView = "dept:view";
    public const string DeptManage = "dept:manage";
    public const string RoleView = "role:view";
    public const string RoleManage = "role:manage";
    public const string FileView = "file:view";
    public const string FileManage = "file:manage";
    public const string AlertView = "alert:view";
    public const string AlertHandle = "alert:handle";
    public const string AuditView = "audit:view";
    public const string AuditExport = "audit:export";
    public const string SettingView = "setting:view";
    public const string SettingManage = "setting:manage";

    public static readonly IReadOnlyList<(string Code, string Name, string Module)> Catalog =
    [
        (UserView, "查看用户", "user"),
        (UserManage, "管理用户", "user"),
        (UserAssignRole, "分配角色", "user"),
        (DeptView, "查看部门", "dept"),
        (DeptManage, "管理部门", "dept"),
        (RoleView, "查看角色", "role"),
        (RoleManage, "管理角色", "role"),
        (FileView, "查看文件", "file"),
        (FileManage, "管理文件", "file"),
        (AlertView, "查看报警", "alert"),
        (AlertHandle, "处理报警", "alert"),
        (AuditView, "查看审计", "audit"),
        (AuditExport, "导出审计", "audit"),
        (SettingView, "查看设置", "setting"),
        (SettingManage, "管理设置", "setting")
    ];
}

/// <summary>
/// 认证数据种子：四预置角色 + 权限目录 + 根部门 + 默认管理员账号。
/// 幂等：已存在则跳过。
/// </summary>
public sealed record PresetRoleSeed(string Code, string Name, DataScope DataScope, IReadOnlyList<string> Permissions);

public static class AuthSeedData
{
    public const string RootDeptCode = "ROOT";
    public const string RootDeptName = "总部";
    public const string AdminRoleCode = "admin";
    public const string ManagerRoleCode = "manager";
    public const string OperatorRoleCode = "operator";
    public const string AuditorRoleCode = "auditor";

    public static readonly IReadOnlyList<PresetRoleSeed> PresetRoles =
    [
        new(AdminRoleCode, "管理员", DataScope.All,
            PermissionCodes.Catalog.Select(p => p.Code).ToArray()),
        new(ManagerRoleCode, "部门负责人", DataScope.DeptAndChildren,
            [PermissionCodes.UserView, PermissionCodes.DeptView, PermissionCodes.RoleView,
             PermissionCodes.FileView, PermissionCodes.FileManage,
             PermissionCodes.AlertView, PermissionCodes.AlertHandle,
             PermissionCodes.AuditView, PermissionCodes.SettingView]),
        new(OperatorRoleCode, "操作员", DataScope.Self,
            [PermissionCodes.FileView, PermissionCodes.FileManage, PermissionCodes.AlertView]),
        new(AuditorRoleCode, "审计员", DataScope.All,
            [PermissionCodes.FileView, PermissionCodes.AlertView, PermissionCodes.AuditView, PermissionCodes.AuditExport])
    ];

    public static readonly Type[] EntityTypes =
    [
        typeof(Account),
        typeof(User),
        typeof(Dept),
        typeof(Role),
        typeof(Permission),
        typeof(RolePermission),
        typeof(UserRole),
        typeof(AuditLog)
    ];
}
