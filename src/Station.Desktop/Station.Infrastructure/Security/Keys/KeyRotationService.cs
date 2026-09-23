using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.Audit;
using Station.Application.Security.Abstractions;
using Station.Domain.Entities;
using Station.Domain.Security;
using Station.Infrastructure.Settings;

namespace Station.Infrastructure.Security.Keys;

/// <summary>
/// 密钥轮换服务：生成新主密钥版本，重加密所有 IsEncrypted=1 的配置字段，写审计。
/// 全流程在事务外逐条进行，失败不阻塞其他记录（返回 FailedCount）。
/// </summary>
public sealed class KeyRotationService : IKeyRotationService
{
    private readonly IMasterKeyProvider _keyProvider;
    private readonly ICryptoProviderFactory _factory;
    private readonly ICryptoPolicyService _policy;
    private readonly ISqlSugarClient _db;
    private readonly ISettingStore _settingStore;
    private readonly IAuditLogService _audit;
    private readonly ILogger<KeyRotationService> _logger;

    public KeyRotationService(
        IMasterKeyProvider keyProvider,
        ICryptoProviderFactory factory,
        ICryptoPolicyService policy,
        ISqlSugarClient db,
        ISettingStore settingStore,
        IAuditLogService audit,
        ILogger<KeyRotationService> logger)
    {
        _keyProvider = keyProvider;
        _factory = factory;
        _policy = policy;
        _db = db;
        _settingStore = settingStore;
        _audit = audit;
        _logger = logger;
    }

    public async Task<KeyRotationResult> RotateAsync(string operatorAccount, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var oldVersion = _keyProvider.CurrentVersion;
        var newVersion = await _keyProvider.RotateAsync(ct).ConfigureAwait(false);

        var policy = await _policy.GetAsync(CryptoUsage.SecretField, ct).ConfigureAwait(false);
        var encryptor = _factory.GetEncryptor(policy.Algorithm);

        var encrypted = await _db.Queryable<SysSetting>()
            .Where(s => s.IsEncrypted)
            .ToListAsync(ct).ConfigureAwait(false);

        var ok = 0;
        var fail = 0;
        foreach (var row in encrypted)
        {
            try
            {
                // 旧密钥解密（旧密文头部带旧版本号，Encryptor 内部自动路由）
                var plain = encryptor.Decrypt(row.ValueJson ?? string.Empty, row.GroupKey + "." + row.SubKey);
                // 用新密钥加密
                var cipher = encryptor.Encrypt(plain, row.GroupKey + "." + row.SubKey);
                row.ValueJson = cipher;
                row.Version += 1;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = operatorAccount;
                await _db.Updateable(row).ExecuteCommandAsync(ct).ConfigureAwait(false);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                _logger.LogError(ex, "重加密失败：{Group}.{Key}", row.GroupKey, row.SubKey);
            }
        }

        // 审计
        await _audit.WriteAsync(new AuditLog
        {
            OperationType = "MasterKeyRotate",
            Target = "crypto.master_key",
            Detail = $"{{\"oldVersion\":{oldVersion},\"newVersion\":{newVersion},\"ok\":{ok},\"fail\":{fail}}}",
            Result = fail == 0 ? 1 : 0,
            OperatorAccount = operatorAccount,
            CreatedAt = DateTime.UtcNow,
            ClientInfo = "Desktop"
        }).ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation("主密钥轮换完成：v{Old}→v{New}，重加密 {Ok}/{Total}",
            oldVersion, newVersion, ok, ok + fail);

        return new KeyRotationResult(oldVersion, newVersion, ok, fail, sw.Elapsed);
    }
}
