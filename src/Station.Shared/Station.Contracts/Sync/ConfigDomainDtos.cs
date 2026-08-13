namespace Station.Contracts.Sync;

/// <summary>
/// 用户域配置同步行 DTO（组织/用户/账号/角色/用户角色/记录仪白名单）。
/// 载荷以自然键（Code/UserNo/UserName/SerialNumber）跨库传递，两端各自落库并重建本地外键，
/// 平台雪花主键不直接同步到采集站本地，避免跨库主键冲突。
/// </summary>

/// <summary>部门行：ParentCode 为跨库父级自然键（ROOT 为 null）。</summary>
public sealed record DeptSyncRow(
    string Code,
    string Name,
    string? ParentCode,
    int SortOrder,
    bool IsActive);

/// <summary>用户行：DeptCode 跨库引用部门自然键。</summary>
public sealed record UserSyncRow(
    string UserNo,
    string Name,
    string DeptCode,
    bool IsActive);

/// <summary>账号行：UserNo 关联用户；密码哈希 sm3$迭代$盐 跨库直接透传。</summary>
public sealed record AccountSyncRow(
    string UserName,
    string PasswordHash,
    string? UserNo,
    bool IsEnabled,
    int FailedLoginAttempts,
    DateTime? LockedUntil);

/// <summary>角色行：PermissionCodes 为权限点编码（跨库稳定）。</summary>
public sealed record RoleSyncRow(
    string Code,
    string Name,
    int DataScope,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes);

/// <summary>用户-角色关联行（派生表，全量重建）。</summary>
public sealed record UserRoleSyncRow(
    string UserNo,
    string RoleCode);

/// <summary>记录仪行（白名单/绑定）：BoundUserNo、DeptCode 为自然键。</summary>
public sealed record RecorderSyncRow(
    string SerialNumber,
    string Model,
    int Protocol,
    string? BoundUserNo,
    string? DeptCode,
    bool IsAuthorized,
    bool IsActive);

/// <summary>领域快照载荷：{"rows": [...]}，一个变更携带某实体类型的全量快照。</summary>
public static class ConfigDomainPayload
{
    public const string EntityTypeDept = "Dept";
    public const string EntityTypeUser = "User";
    public const string EntityTypeAccount = "Account";
    public const string EntityTypeRole = "Role";
    public const string EntityTypeUserRole = "UserRole";
    public const string EntityTypeRecorder = "Recorder";

    /// <summary>平台发布顺序（本地应用依赖顺序：先部门后用户/角色，再账号/记录仪）。</summary>
    public static readonly IReadOnlyList<string> PublishOrder =
    [
        EntityTypeDept,
        EntityTypeUser,
        EntityTypeRole,
        EntityTypeUserRole,
        EntityTypeAccount,
        EntityTypeRecorder
    ];
}
