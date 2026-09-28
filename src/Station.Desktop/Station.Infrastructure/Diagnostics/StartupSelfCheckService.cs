using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Diagnostics;
using Station.Application.Licensing;
using Station.Application.Security;
using Station.Application.Storage;
using Station.Data.Abstractions;
using Station.Crypto;
using Station.Infrastructure.Security.Keys;

namespace Station.Infrastructure.Diagnostics;

/// <summary>
/// 启动自检实现（适配优化后架构）。
/// 
/// 检查项：
///   1) 数据库连接
///   2) 加密策略表可读
///   3) 主密钥来源（文件 / 环境变量）
///   4) 主密钥加解密往返
///   5) 授权公钥文件
///   6) 存储目标配置可读
/// </summary>
public sealed class StartupSelfCheckService : IStartupSelfCheckService
{
    /// <summary>必须存在的加密用途。</summary>
    private static readonly string[] RequiredUsages =
    {
        CryptoUsage.Password,
        CryptoUsage.License,
        CryptoUsage.FileSig,
        CryptoUsage.SecretField,
        CryptoUsage.FileEncryption,
        CryptoUsage.RecorderBinding,
        CryptoUsage.Reporting
    };

    private readonly IDatabaseHealthService _dbHealth;
    private readonly ICryptoPolicyService _policy;
    private readonly MasterKeyProvider _keyProvider;
    private readonly PemKeyCache _pemCache;
    private readonly IStorageConfigStore _storageConfig;
    private readonly LicenseOptions _licenseOptions;
    private readonly ILogger<StartupSelfCheckService> _logger;

    public StartupSelfCheckService(
        IDatabaseHealthService dbHealth,
        ICryptoPolicyService policy,
        MasterKeyProvider keyProvider,
        PemKeyCache pemCache,
        IStorageConfigStore storageConfig,
        IOptions<LicenseOptions> licenseOptions,
        ILogger<StartupSelfCheckService> logger)
    {
        _dbHealth = dbHealth;
        _policy = policy;
        _keyProvider = keyProvider;
        _pemCache = pemCache;
        _storageConfig = storageConfig;
        _licenseOptions = licenseOptions.Value;
        _logger = logger;
    }

    public async Task<SelfCheckReport> RunAsync(CancellationToken ct = default)
    {
        var items = new List<SelfCheckItem>();

        items.Add(await CheckDatabaseAsync(ct));
        items.Add(await CheckPolicyTableAsync(ct));
        items.Add(CheckMasterKeySource());
        items.Add(await CheckMasterKeyRoundTripAsync(ct));
        items.Add(await CheckLicensePublicKeyAsync(ct));
        items.Add(await CheckStorageConfigAsync(ct));

        var healthy = items.All(i => i.Ok);
        _logger.Log(healthy ? LogLevel.Information : LogLevel.Error,
            "启动自检完成：{Status}（{Failed}/{Total} 项失败）",
            healthy ? "通过" : "未通过",
            items.Count(i => !i.Ok), items.Count);

        return new SelfCheckReport(healthy, items, DateTime.UtcNow);
    }

    // =========================================================
    // 1) 数据库连接
    // =========================================================

    private async Task<SelfCheckItem> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var connected = await _dbHealth.IsConnected();
            return connected
                ? new SelfCheckItem("数据库连接", true, "连接正常")
                : new SelfCheckItem("数据库连接", false, "数据库未连接",
                    "检查数据库服务是否启动、连接串是否正确");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("数据库连接", false, ex.Message,
                "检查数据库服务是否启动、连接串是否正确");
        }
    }

    // =========================================================
    // 2) 加密策略表
    // =========================================================

    private async Task<SelfCheckItem> CheckPolicyTableAsync(CancellationToken ct)
    {
        try
        {
            var all = await _policy.GetAllAsync(ct);
            if (all.Count == 0)
                return new SelfCheckItem("加密策略表", false, "策略表为空",
                    "运行数据库初始化脚本或联系管理员");

            var existing = all
                .Select(x => x.UsageCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = RequiredUsages
                .Where(u => !existing.Contains(u))
                .ToList();

            if (missing.Count > 0)
                return new SelfCheckItem("加密策略表", false,
                    $"缺少策略：{string.Join(", ", missing)}",
                    "在设置页补充加密策略或重置为出厂默认");

            return new SelfCheckItem("加密策略表", true,
                $"已加载 {all.Count} 条策略（{RequiredUsages.Length} 个必需用途齐全）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("加密策略表", false, ex.Message,
                "检查 station_crypto_policy 表是否存在");
        }
    }

    // =========================================================
    // 3) 主密钥来源
    // =========================================================

    private SelfCheckItem CheckMasterKeySource()
    {
        try
        {
            if (_keyProvider.IsUsingEnvironmentVariable)
                return new SelfCheckItem("主密钥来源", true,
                    $"环境变量（v{_keyProvider.CurrentVersion}）");

            var path = _keyProvider.KeyFilePath;
            if (!File.Exists(path))
                return new SelfCheckItem("主密钥来源", false,
                    $"文件不存在：{path}",
                    "首次启动会自动生成；若已启动过，请检查文件是否被删除");

            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
                return new SelfCheckItem("主密钥来源", false,
                    $"文件内容为空：{path}",
                    "删除后重启应用以重新生成");

            return new SelfCheckItem("主密钥来源", true,
                $"密钥文件 v{_keyProvider.CurrentVersion}：{path}");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("主密钥来源", false, ex.Message,
                "检查文件权限与磁盘空间");
        }
    }

    // =========================================================
    // 4) 主密钥加解密往返
    // =========================================================

    private async Task<SelfCheckItem> CheckMasterKeyRoundTripAsync(CancellationToken ct)
    {
        try
        {
            // 走策略服务取 Encryptor（内部自动路由算法）
            var encryptor = await _policy.GetEncryptorAsync(CryptoUsage.SecretField, ct)
                .ConfigureAwait(false);
            var snapshot = await _policy.GetAsync(CryptoUsage.SecretField, ct)
                .ConfigureAwait(false);

            const string probe = "__self_check_probe__";
            var cipher = encryptor.Encrypt(probe, aad: "selfcheck.probe");
            var plain = encryptor.Decrypt(cipher, aad: "selfcheck.probe");

            if (plain != probe)
                return new SelfCheckItem("主密钥加解密", false,
                    "往返测试结果不一致",
                    "主密钥文件可能损坏，请从备份恢复");

            return new SelfCheckItem("主密钥加解密", true,
                $"算法 {snapshot.Algorithm}，往返正常（v{_keyProvider.CurrentVersion}）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("主密钥加解密", false, ex.Message,
                "主密钥文件可能损坏；如无法恢复，需删除密钥文件并重建（会丢失已加密数据）");
        }
    }

    // =========================================================
    // 5) 授权公钥文件
    // =========================================================

    private async Task<SelfCheckItem> CheckLicensePublicKeyAsync(CancellationToken ct)
    {
        try
        {
            var path = _licenseOptions.PublicKeyFile;
            if (string.IsNullOrWhiteSpace(path))
                return new SelfCheckItem("授权公钥", false, "未配置公钥文件路径",
                    "在 appsettings.json 的 Station:License:PublicKeyFile 中配置");

            var pem = await _pemCache.GetAsync(path, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pem))
                return new SelfCheckItem("授权公钥", false,
                    $"文件不存在或为空：{path}",
                    "从授权工具方获取公钥文件并放到指定位置");

            return new SelfCheckItem("授权公钥", true, $"已加载：{path}");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("授权公钥", false, ex.Message, null);
        }
    }

    // =========================================================
    // 6) 存储目标配置
    // =========================================================

    private async Task<SelfCheckItem> CheckStorageConfigAsync(CancellationToken ct)
    {
        try
        {
            var targets = await _storageConfig.GetAllAsync(ct).ConfigureAwait(false);
            var enabled = targets.Count(t => t.Enabled);

            if (enabled == 0)
                return new SelfCheckItem("存储目标配置", false, "没有启用的存储目标",
                    "在设置页启用至少一个存储目标");

            return new SelfCheckItem("存储目标配置", true,
                $"{enabled} 个启用 / 共 {targets.Count} 个");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("存储目标配置", false, ex.Message,
                "检查 station_sys_setting 表");
        }
    }
}
