using Station.Domain.Entities;

namespace Station.Infrastructure.Settings;

/// <summary>
/// 配置持久化接口：读写 SysSetting，自动处理 IsEncrypted=1 字段的加解密。
/// 加解密使用 secret_field 策略选定的算法（默认 SM4-GCM）。
/// </summary>
public interface ISettingStore
{
    Task<string?> GetRawAsync(string group, string key, CancellationToken ct = default);
    Task SetRawAsync(string group, string key, string? valueJson, bool secret,
        string operatorAccount, CancellationToken ct = default);

    /// <summary>批量写入同组配置（事务）。</summary>
    Task SetGroupAsync(string group, IReadOnlyDictionary<string, (string? Json, bool Secret)> values,
        string operatorAccount, CancellationToken ct = default);

    /// <summary>获取整组配置，secret 字段自动解密。</summary>
    Task<Dictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default);

    Task<List<SysSetting>> GetAllAsync(CancellationToken ct = default);
}
