using Microsoft.Extensions.Logging;
using Station.Application.Security;
using Station.Domain.Entities;
using Station.Data.Repositories;
using Station.Data.SqlSugar.Extensions;

namespace Station.Infrastructure.Settings;

/// <summary>
/// 配置持久化：station_sys_setting 表读写 + 敏感字段透明加解密。
/// 
/// 【设计要点】
///   1) 加解密逻辑统一委托给 CryptoSecretExtensions（唯一实现来源）；
///   2) AAD 由 "{group}.{key}" 派生，与扩展方法保持一致；
///   3) secret: true 语义 = "传入的是明文，请帮我加密后存储"；
///      secret: false 语义 = "传入的已是最终存储值，原样写入"；
///   4) 解密路径尽力而为：单字段失败返回 null，不阻塞整组读取。
/// 
/// 【与 CryptoSecretExtensions 的关系】
///   本类不自己实现 AAD 派生、v{n}: 判断、版本路由，
///   全部通过 _factory 上的扩展方法完成。任何加解密逻辑变更只需改扩展方法。
/// </summary>
public sealed class SettingStore : ISettingStore
{
    private readonly IRepository<SysSetting> _settings;
    private readonly ICryptoPolicyService _policy;
    private readonly ILogger<SettingStore> _logger;

    public SettingStore(
        IRepository<SysSetting> settings,
        ICryptoPolicyService policy,
        ILogger<SettingStore> logger)
    {
        _settings = settings;
        _policy = policy;
        _logger = logger;
    }

    /// <summary>
    /// 单键读取
    /// </summary>
    public async Task<string?> GetRawAsync(string group, string key, CancellationToken ct = default)
    {
        var entity = await _settings.AsQueryable()
            .Where(s => s.GroupKey == group && s.SubKey == key)
            .FirstAsync(ct).ConfigureAwait(false);

        if (entity is null) return null;

        // 非加密字段：直接返回值
        if (!entity.IsEncrypted) return entity.ValueJson;

        // 加密字段：走扩展方法解密（内部处理历史明文兼容 + 失败降级）
        return await _policy.TryUnprotectSecretAsync(group, key, entity.ValueJson, _logger, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 单键写入
    /// </summary>
    public async Task SetRawAsync(
        string group,
        string key,
        string? valueJson,
        bool secret,
        string operatorAccount,
        CancellationToken ct = default)
    {
        // ---- secret: true 时先加密明文 ----
        // 注意：加密失败会向上抛（写不进去就是写不进去）
        var storedValue = valueJson;
        if (secret && valueJson is not null)
        {
            storedValue = await _policy.ProtectSecretAsync(group, key, valueJson, _logger, ct).ConfigureAwait(false);
        }

        // ---- 落库（存在则更新，不存在则插入） ----
        var existing = await _settings.AsQueryable()
            .Where(s => s.GroupKey == group && s.SubKey == key)
            .FirstAsync(ct).ConfigureAwait(false);

        if (existing is null)
        {
            await _settings.InsertAsync(new SysSetting
            {
                GroupKey = group,
                SubKey = key,
                ValueJson = storedValue,
                ValueType = secret ? "secret" : "string",
                IsEncrypted = secret,
                Version = 1,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = operatorAccount
            }).ConfigureAwait(false);
        }
        else
        {
            existing.ValueJson = storedValue;
            existing.IsEncrypted = secret;
            existing.ValueType = secret ? "secret" : existing.ValueType;
            existing.Version += 1;   // 乐观锁计数
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = operatorAccount;

            await _settings.UpdateAsync(existing).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 批量写入
    /// </summary>
    /// <inheritdoc />
    /// <remarks>
    /// 注意：本方法不保证事务性（逐条写入）。如需事务，请在调用方外包事务，
    /// 或改用底层 _db 的 UseTran 包裹。
    /// </remarks>
    public async Task SetGroupAsync(
        string group,
        IReadOnlyDictionary<string, (string? Json, bool Secret)> values,
        string operatorAccount,
        CancellationToken ct = default)
    {
        foreach (var (key, (json, secret)) in values)
        {
            await SetRawAsync(group, key, json, secret, operatorAccount, ct)
                .ConfigureAwait(false);
        }
    }




    /// <summary>
    /// 整组读取
    /// </summary>
    public async Task<Dictionary<string, string?>> GetGroupAsync(
        string group, CancellationToken ct = default)
    {
        var list = await _settings.AsQueryable()
            .Where(s => s.GroupKey == group)
            .ToListAsync(ct).ConfigureAwait(false);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in list)
        {
            if (e.IsEncrypted)
            {
                // 加密字段：走扩展方法（失败返回 null，不影响其他字段读取）
                result[e.SubKey] = await _policy.TryUnprotectSecretAsync(group, e.SubKey, e.ValueJson, _logger, ct).ConfigureAwait(false);
            }
            else
            {
                result[e.SubKey] = e.ValueJson;
            }
        }

        return result;
    }

    // =========================================================
    // 全量导出（运维/迁移用）
    // =========================================================

    /// <inheritdoc />
    /// <remarks>
    /// 返回的实体带密文原值（不自动解密），供备份/迁移工具使用。
    /// 如需明文，请自行按字段调用 TryUnprotectSecretAsync。
    /// </remarks>
    public Task<List<SysSetting>> GetAllAsync(CancellationToken ct = default) =>
        _settings.GetListAsync(ct: ct);
}
