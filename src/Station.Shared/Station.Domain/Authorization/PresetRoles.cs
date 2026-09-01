using Station.Domain.Enums;

namespace Station.Domain.Authorization;

public sealed record RoleSeed(string Code, string Name, DataScope DataScope, IReadOnlyList<string> Permissions);

/// <summary>
/// 认证数据种子：四预置角色 + 权限目录 + 根部门 + 默认管理员账号。
/// 幂等：已存在则跳过。
/// </summary>
public static class PresetRoles
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Operator = "operator";
    public const string Auditor = "auditor";

    public static readonly IReadOnlyList<RoleSeed> List = new[]
    {
        new RoleSeed(Admin, "管理员", DataScope.All,
            PermissionCodes.Catalog.Select(p => p.Code).ToArray()),
        new RoleSeed(Manager, "部门负责人", DataScope.DeptAndChildren,
            new[]
            {
                PermissionCodes.UserView,
                PermissionCodes.DeptView,
                PermissionCodes.RoleView,
                PermissionCodes.StationView,
                PermissionCodes.RecorderView,
                PermissionCodes.FileView,
                PermissionCodes.FileManage,
                PermissionCodes.AlertView,
                PermissionCodes.AlertHandle,
                PermissionCodes.AuditView,
                PermissionCodes.SettingView
            }),
        new RoleSeed(Operator, "操作员", DataScope.Self,
            new[] { PermissionCodes.FileView, PermissionCodes.FileManage, PermissionCodes.AlertView }),
        new RoleSeed(Auditor, "审计员", DataScope.All,
            new[]
            {
                PermissionCodes.FileView,
                PermissionCodes.AlertView,
                PermissionCodes.AuditView,
                PermissionCodes.AuditExport,
                PermissionCodes.RecorderView
            })
    };
}
