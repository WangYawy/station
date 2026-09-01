namespace Station.Domain.Authorization;

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
    public const string StationView = "station:view";
    public const string StationManage = "station:manage";
    public const string RecorderView = "recorder:view";
    public const string RecorderManage = "recorder:manage";

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
        (SettingManage, "管理设置", "setting"),
        (StationView, "查看采集站", "station"),
        (StationManage, "管理采集站", "station"),
        (RecorderView, "查看记录仪", "recorder"),
        (RecorderManage, "管理记录仪", "recorder")
    ];
}
