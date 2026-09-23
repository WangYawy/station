namespace Station.Application.Storage;

/// <summary>
/// 存储目标配置的持久化。
/// 后台：station_sys_setting（GroupKey="storage", SubKey="targets"）。
/// 首次启动 seed：从 appsettings.json 的 Station:Storage:Targets 读入。
/// </summary>
public interface IStorageConfigStore
{
    /// <summary>读取当前有效配置（缓存）。</summary>
    Task<IReadOnlyList<StorageTargetConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 覆盖保存所有目标。
    /// 密码字段处理规则：
    ///   - 明文非空 → 加密后存；
    ///   - 明文 null 或空 → 保留 DB 中已有的密文（"不修改"语义）。
    /// </summary>
    Task SaveAllAsync(
        IReadOnlyList<StorageTargetConfig> targets,
        string operatorAccount,
        CancellationToken ct = default);

    /// <summary>重置为 appsettings 种子（应急用），写审计。</summary>
    Task ResetToSeedAsync(string operatorAccount, CancellationToken ct = default);

    /// <summary>首次启动时从 appsettings seed（幂等：已有配置则跳过）。</summary>
    Task SeedIfEmptyAsync(CancellationToken ct = default);
}
