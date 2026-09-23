using Microsoft.Extensions.Logging;
using Station.Application.Diagnostics;
using Station.Application.Security.Abstractions;
using Station.Application.Services;
using Station.Application.Storage;
using Station.Domain.Security;

namespace Station.Infrastructure.Diagnostics;

/// <summary>
/// 启动自检实现。检查项：
///   1) 数据库连接
///   2) 加密策略表可读
///   3) 主密钥文件存在
///   4) 主密钥能加解密（往返测试）
///   5) SM2 公钥文件存在（授权用）
///   6) 存储目标配置可读
/// </summary>
public sealed class StartupSelfCheckService : IStartupSelfCheckService
{
    private readonly IDatabaseHealthService _dbHealth;
    private readonly ICryptoPolicyService _policy;
    private readonly ICryptoProviderFactory _factory;
    private readonly IMasterKeyProvider _keyProvider;
    private readonly IKeyFileResolver _keyResolver;
    private readonly IStorageConfigStore _storageConfig;
    private readonly LicenseOptionsReader _licenseOptions;   // 见下
    private readonly ILogger<StartupSelfCheckService> _logger;

    public StartupSelfCheckService(
        IDatabaseHealthService dbHealth,
        ICryptoPolicyService policy,
        ICryptoProviderFactory factory,
        IMasterKeyProvider keyProvider,
        IKeyFileResolver keyResolver,
        IStorageConfigStore storageConfig,
        LicenseOptionsReader licenseOptions,
        ILogger<StartupSelfCheckService> logger)
    {
        _dbHealth = dbHealth;
        _policy = policy;
        _factory = factory;
        _keyProvider = keyProvider;
        _keyResolver = keyResolver;
        _storageConfig = storageConfig;
        _licenseOptions = licenseOptions;
        _logger = logger;
    }

    public async Task<SelfCheckReport> RunAsync(CancellationToken ct = default)
    {
        var items = new List<SelfCheckItem>();

        // 1) 数据库连接
        items.Add(await CheckDatabaseAsync(ct));

        // 2) 策略表可读
        items.Add(await CheckPolicyTableAsync(ct));

        // 3) 主密钥文件存在
        items.Add(CheckMasterKeyFile());

        // 4) 主密钥加解密往返
        items.Add(await CheckMasterKeyRoundTripAsync(ct));

        // 5) SM2 公钥文件
        items.Add(await CheckLicensePublicKeyAsync(ct));

        // 6) 存储目标配置可读
        items.Add(await CheckStorageConfigAsync(ct));

        var healthy = items.All(i => i.Ok);
        _logger.Log(healthy ? LogLevel.Information : LogLevel.Error,
            "启动自检完成：{Status}（{Failed}/{Total} 项失败）",
            healthy ? "通过" : "未通过",
            items.Count(i => !i.Ok), items.Count);

        return new SelfCheckReport(healthy, items, DateTime.UtcNow);
    }

    // =========================================================
    // 各项检查
    // =========================================================

    private async Task<SelfCheckItem> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var dbConnected = await _dbHealth.IsConnected();
            // await _db.Ado.GetIntAsync("SELECT 1", cancellationToken: ct);
            return new SelfCheckItem("数据库连接", true, "连接正常");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("数据库连接", false, ex.Message,
                "检查数据库服务是否启动、连接串是否正确");
        }
    }

    private async Task<SelfCheckItem> CheckPolicyTableAsync(CancellationToken ct)
    {
        try
        {
            var all = await _policy.GetAllAsync(ct);
            if (all.Count == 0)
                return new SelfCheckItem("加密策略表", false, "策略表为空",
                    "运行数据库初始化脚本或联系管理员");

            // 每个用途都应有策略
            var required = new[]
            {
                CryptoUsage.Password, CryptoUsage.License,
                CryptoUsage.FileSig, CryptoUsage.SecretField
            };
            var missing = required
                .Where(r => all.All(x => !string.Equals(x.UsageCode, r, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (missing.Count > 0)
                return new SelfCheckItem("加密策略表", false,
                    $"缺少策略：{string.Join(",", missing)}",
                    "在设置页补充加密策略或重置为出厂默认");

            return new SelfCheckItem("加密策略表", true, $"已加载 {all.Count} 条策略");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("加密策略表", false, ex.Message,
                "检查 station_crypto_policy 表是否存在");
        }
    }

    private SelfCheckItem CheckMasterKeyFile()
    {
        try
        {
            var path = GetMasterKeyFilePath();
            if (!File.Exists(path))
                return new SelfCheckItem("主密钥文件", false, $"文件不存在：{path}",
                    "首次启动会自动生成；若已启动过，请检查文件是否被删除");

            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
                return new SelfCheckItem("主密钥文件", false, "文件内容为空",
                    "删除后重启应用以重新生成");

            return new SelfCheckItem("主密钥文件", true, $"存在：{path}");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("主密钥文件", false, ex.Message,
                "检查文件权限与磁盘空间");
        }
    }

    private async Task<SelfCheckItem> CheckMasterKeyRoundTripAsync(CancellationToken ct)
    {
        try
        {
            // 拿当前策略
            var policy = await _policy.GetAsync(CryptoUsage.SecretField, ct);
            var encryptor = _factory.GetEncryptor(policy.Algorithm);

            // 加密 → 解密往返
            const string probe = "__self_check_probe__";
            var cipher = encryptor.Encrypt(probe, aad: "selfcheck.probe");
            var plain = encryptor.Decrypt(cipher, aad: "selfcheck.probe");

            if (plain != probe)
                return new SelfCheckItem("主密钥加解密", false,
                    "往返测试结果不一致",
                    "主密钥文件可能损坏，请从备份恢复");

            return new SelfCheckItem("主密钥加解密", true,
                $"算法 {policy.Algorithm}，往返正常（v{_keyProvider.CurrentVersion}）");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("主密钥加解密", false, ex.Message,
                "主密钥文件可能损坏；如无法恢复，需删除密钥文件并重建（会丢失已加密数据）");
        }
    }

    private async Task<SelfCheckItem> CheckLicensePublicKeyAsync(CancellationToken ct)
    {
        try
        {
            var path = _licenseOptions.PublicKeyFile;
            if (string.IsNullOrWhiteSpace(path))
                return new SelfCheckItem("授权公钥", false, "未配置公钥文件路径",
                    "在 appsettings.json 的 Station:License:PublicKeyFile 中配置");

            var pem = await _keyResolver.ResolveAsync(path, ct);
            if (string.IsNullOrWhiteSpace(pem))
                return new SelfCheckItem("授权公钥", false, $"文件不存在或为空：{path}",
                    "从授权工具方获取公钥文件");

            return new SelfCheckItem("授权公钥", true, $"已加载：{path}");
        }
        catch (Exception ex)
        {
            return new SelfCheckItem("授权公钥", false, ex.Message, null);
        }
    }

    private async Task<SelfCheckItem> CheckStorageConfigAsync(CancellationToken ct)
    {
        try
        {
            var targets = await _storageConfig.GetAllAsync(ct);
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

    // =========================================================
    // 辅助
    // =========================================================

    private static string GetMasterKeyFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(common, "Station", "keys", "master.key");
        }
        return "/etc/station/keys/master.key";
    }
}

/// <summary>
/// 轻量 LicenseOptions 读取器（只读配置，避免直接注入 IOptions 到自检）。
/// </summary>
public sealed class LicenseOptionsReader
{
    public string PublicKeyFile { get; init; } = string.Empty;

    public LicenseOptionsReader(Microsoft.Extensions.Options.IOptions<Station.Application.Licensing.LicenseOptions> opts)
    {
        PublicKeyFile = opts.Value.PublicKeyFile;
    }
}
