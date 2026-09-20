namespace Station.Domain.Audit;

/// <summary>
/// 审计操作类型受控词表。<br/>
/// 操作类型由应用代码写入，因此在这里集中定义，不从数据反推。<br/>
/// 新增类型：加常量 + 往 <see cref="All"/> 里加一条，界面自动可见。
/// </summary>
public static class AuditOperationTypes
{
    public const string Login = "login";
    public const string BackupAuto = "backup.auto";
    public const string CacheCleanup = "cache.cleanup";

    /// <summary>全部内置类型，按 SortOrder 升序。</summary>
    public static readonly IReadOnlyList<AuditOperationTypeDescriptor> All =
    [
        new(Login,        "登录",     "认证", 10),
        new(BackupAuto,   "自动备份", "备份", 20),
        new(CacheCleanup, "缓存清理", "维护", 30),
    ];

    /// <summary>把存储值翻译成显示名；未登记的 code 原样返回（历史脏数据兜底）。</summary>
    public static string DisplayOf(string code) =>
        All.FirstOrDefault(x => x.Code == code)?.DisplayName ?? code;
}
