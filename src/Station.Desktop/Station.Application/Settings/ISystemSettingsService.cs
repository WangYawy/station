namespace Station.Application.Settings;

/// <summary>
/// 系统设置服务：读取当前生效设置（默认配置 + 运行时覆盖），管理员/赋权用户按分组修改，
/// 可热应用（采集/存储/登录策略直接生效），重启类设置写入运行时文件并在提示中说明。
/// </summary>
public interface ISystemSettingsService
{
    Task<SystemSettingsCoreDto> GetCoreAsync();

    /// <summary>按分组更新设置；返回需要重启生效的提示列表。group: basic/storage/collect。</summary>
    Task<IReadOnlyList<string>> UpdateAsync(
        string group,
        IReadOnlyDictionary<string, string> values,
        string operatorAccount);
}
