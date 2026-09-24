using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Audit;
using Station.Application.Security;
using Station.Application.Storage;
using Station.Domain.Entities;
using Station.Infrastructure.Settings;

namespace Station.Infrastructure.Storage;

/// <summary>
/// 存储目标配置持久化实现（基于 CryptoSecretExtensions 加解密版本）。
/// 
/// 【存储结构】
///   - 位置：station_sys_setting(GroupKey="storage", SubKey="targets")
///   - ValueJson：整个 targets 数组序列化后的 JSON（含密文密码）
///   - 密码字段：FTP/SFTP 密码由 CryptoSecretExtensions 加密后存入 JSON
/// 
/// 【AAD 约定】
///   与 SftpStorageTarget / FtpStorageTarget 解密时保持一致：
///   - FTP 密码：group="storage.targets", key="ftpPassword" → AAD="storage.targets.ftpPassword"
///   - SFTP 密码：group="storage.targets", key="sftpPassword" → AAD="storage.targets.sftpPassword"
/// 
/// 【缓存】
///   进程内 MemoryCache 缓存整个 targets 列表（滑动 2 小时）。
///   SaveAllAsync / ResetToSeedAsync 后主动清缓存。
/// 
/// 【与 SftpStorageTarget/FtpStorageTarget 的关系】
///   - 本类负责：保存时加密密码（写入 DB）
///   - Target 类负责：读取时解密密码（运行时使用）
///   - 两者必须使用同一套 AAD 规则，否则密文无法解密
/// </summary>
public sealed class StorageConfigStore : IStorageConfigStore
{
    // ---- AAD 上下文常量（必须与 Target 类保持一致） ----
    private const string PasswordGroup = "storage.targets";
    private const string FtpPasswordKey = "ftpPassword";
    private const string SftpPasswordKey = "sftpPassword";

    // ---- DB 存储位置常量 ----
    private const string Group = "storage";
    private const string Key = "targets";
    private const string CacheKey = "storage:targets";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISettingStore _settingStore;
    private readonly ICryptoPolicyService _policy;
    private readonly IAuditLogService _audit;
    private readonly IMemoryCache _cache;
    private readonly StorageOptions _seedOptions;
    private readonly ILogger<StorageConfigStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public StorageConfigStore(
        ISettingStore settingStore,
        ICryptoPolicyService policy,
        IAuditLogService audit,
        IMemoryCache cache,
        IOptions<StorageOptions> seedOptions,
        ILogger<StorageConfigStore> logger)
    {
        _settingStore = settingStore;
        _policy = policy;
        _audit = audit;
        _cache = cache;
        _seedOptions = seedOptions.Value;
        _logger = logger;
    }

    // 读取
    /// <inheritdoc />
    /// <remarks>
    /// 返回的配置中密码字段为密文原值（不自动解密）。
    /// 解密由 SftpStorageTarget / FtpStorageTarget 在运行时完成，
    /// 避免每次读取配置都要解密所有密码。
    /// </remarks>
    public async Task<IReadOnlyList<StorageTargetConfig>> GetAllAsync(
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<StorageTargetConfig>? cached)
            && cached is not null)
        {
            return cached;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // double-check：等待锁期间可能其他线程已填充缓存
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null)
                return cached;

            var json = await _settingStore.GetRawAsync(Group, Key, ct).ConfigureAwait(false);

            List<StorageTargetConfig> list;
            if (string.IsNullOrEmpty(json))
            {
                // DB 无配置：使用 appsettings 种子（首次启动场景）
                list = CloneList(_seedOptions.Targets);
                _logger.LogInformation(
                    "存储目标配置未初始化，使用 appsettings 种子（{Count} 项）",
                    list.Count);
            }
            else
            {
                list = JsonSerializer.Deserialize<List<StorageTargetConfig>>(json, JsonOpts)
                       ?? new List<StorageTargetConfig>();
            }

            _cache.Set(CacheKey, (IReadOnlyList<StorageTargetConfig>)list,
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(2)
                });

            return list;
        }
        finally { _lock.Release(); }
    }

    // 保存
    /// <inheritdoc />
    /// <remarks>
    /// 【加密规则】
    ///   - UI 传来的密码是明文 → 本方法负责加密后存入 JSON；
    ///   - 已是密文（IsSecretProtected=true）→ 保持原样，避免双重加密；
    ///   - 空密码 → 保持原样（可能表示"不修改"或"不使用密码"）。
    /// 
    /// 【AAD 规则】
    ///   使用 CryptoSecretExtensions.ProtectSecretAsync，
    ///   group="storage.targets"，key 按字段区分（ftpPassword/sftpPassword）。
    /// </remarks>
    public async Task SaveAllAsync(
        IReadOnlyList<StorageTargetConfig> targets,
        string operatorAccount,
        CancellationToken ct = default)
    {
        var encrypted = new List<StorageTargetConfig>(targets.Count);

        foreach (var t in targets)
        {
            var copy = Clone(t);

            // ---- FTP 密码加密 ----
            // 仅当"非空且非密文"时才加密；已是密文则保持原样
            if (!string.IsNullOrWhiteSpace(copy.FtpPassword)
                && !_policy.IsSecretProtected(copy.FtpPassword))
            {
                copy.FtpPassword = await _policy.ProtectSecretAsync(
                    PasswordGroup,
                    FtpPasswordKey,
                    copy.FtpPassword,
                    _logger,
                    ct).ConfigureAwait(false);
            }

            // ---- SFTP 密码加密 ----
            if (!string.IsNullOrWhiteSpace(copy.SftpPassword)
                && !_policy.IsSecretProtected(copy.SftpPassword))
            {
                copy.SftpPassword = await _policy.ProtectSecretAsync(
                    PasswordGroup,
                    SftpPasswordKey,
                    copy.SftpPassword,
                    _logger,
                    ct).ConfigureAwait(false);
            }

            encrypted.Add(copy);
        }

        // ---- 序列化并写入 ----
        var json = JsonSerializer.Serialize(encrypted, JsonOpts);

        // 注意：secret: false，因为密码字段已在本方法内加密完毕
        await _settingStore.SetRawAsync(Group, Key, json, secret: false, operatorAccount, ct)
            .ConfigureAwait(false);

        // 清缓存，下次读取拿到新配置
        _cache.Remove(CacheKey);

        // ---- 审计 ----
        await _audit.WriteAsync(new AuditLog
        {
            OperationType = "StorageConfigUpdate",
            Target = "storage.targets",
            Detail = $"{{\"count\":{targets.Count}}}",
            Result = 1,
            CreatedAt = DateTime.UtcNow,
            OperatorAccount = operatorAccount,
            ClientInfo = "Desktop"
        }, ct).ConfigureAwait(false);

        _logger.LogInformation("存储目标配置已更新（{Count} 项）", targets.Count);
    }

    // 重置为种子
    /// <inheritdoc />
    public async Task ResetToSeedAsync(string operatorAccount, CancellationToken ct = default)
    {
        await SaveAllAsync(_seedOptions.Targets, operatorAccount, ct).ConfigureAwait(false);
        _logger.LogWarning("存储目标配置已重置为 appsettings 种子");
    }

    // 首次启动 seed
    /// <inheritdoc />
    /// <remarks>
    /// 幂等：DB 已有配置则跳过。
    /// 由 App.axaml.cs 在启动自检之前调用。
    /// </remarks>
    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        var json = await _settingStore.GetRawAsync(Group, Key, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(json)) return;

        await SaveAllAsync(_seedOptions.Targets, "system:seed", ct).ConfigureAwait(false);
        _logger.LogInformation("存储目标配置已从 appsettings 首次 seed 入 DB");
    }

    // 克隆（防止污染调用方对象）
    private static StorageTargetConfig Clone(StorageTargetConfig src) => new()
    {
        Kind = src.Kind,
        Name = src.Name,
        Enabled = src.Enabled,
        IsPrimary = src.IsPrimary,
        LocalRoot = src.LocalRoot,
        FtpHost = src.FtpHost,
        FtpPort = src.FtpPort,
        FtpUser = src.FtpUser,
        FtpPassword = src.FtpPassword,
        SftpHost = src.SftpHost,
        SftpPort = src.SftpPort,
        SftpUser = src.SftpUser,
        SftpPassword = src.SftpPassword,
        SftpRoot = src.SftpRoot,
        CircuitBreakerThreshold = src.CircuitBreakerThreshold,
        CircuitBreakerCooldownSeconds = src.CircuitBreakerCooldownSeconds,
        RetryCount = src.RetryCount,
        RetryIntervalSeconds = src.RetryIntervalSeconds,
        RemoteVerifyMode = src.RemoteVerifyMode,
        FtpKeepAliveSeconds = src.FtpKeepAliveSeconds,
        SftpKeepAliveSeconds = src.SftpKeepAliveSeconds,
        SftpPoolSize = src.SftpPoolSize
    };

    private static List<StorageTargetConfig> CloneList(
        IReadOnlyList<StorageTargetConfig> src) =>
        src.Select(Clone).ToList();
}
