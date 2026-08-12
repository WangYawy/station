namespace Station.Desktop.Application.OperationAccess;

/// <summary>
/// 操作权限配置：哪些模块的操作需要登录后才能执行（对应原型"操作权限管理"）。
/// 需求基线：必须登录模式下所有导航与主界面操作按钮均需登录，默认全部要求；
/// 可按原型默认调整为仅"设置"要求登录。
/// </summary>
public sealed class OperationAuthOptions
{
    public const string SectionName = "Station:OpAuth";

    /// <summary>需要登录的模块键集合（workbench/collect/history/logs/settings）。</summary>
    public List<string> RequiredModules { get; set; } =
    [
        "workbench",
        "collect",
        "history",
        "logs",
        "settings"
    ];
}
