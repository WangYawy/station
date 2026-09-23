using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Station.Application.Security.Abstractions;
using Station.Domain.Security;

namespace Station.Application.Security;

/// <summary>
/// 敏感配置字段加解密扩展方法。
/// 
/// 【设计目标】
///   将"读 SecretField 策略 → 取 Encryptor → 加密/解密"这一固定套路收敛为
///   三个静态扩展方法，作为业务代码使用敏感字段加解密的唯一入口。
/// 
/// 【护栏】
///   1) AAD 强制由 "{group}.{key}" 派生，调用方无法自定义 —— 防止读写不一致；
///   2) 密文格式 v{n}:base64 统一识别 —— 保证历史明文/密文混存兼容；
///   3) 解密失败降级返回 null + ERROR 日志，不抛异常 —— 单字段损坏不阻塞整体；
///   4) 加密失败向上抛 —— 写不进去就是写不进去，静默更糟。
/// 
/// 【适用边界】
///   仅服务 CryptoUsage.SecretField 用途。其他用途（License/密码/文件摘要）
///   语义不同，请另开扩展组，不要复用本方法。
/// 
/// 【与 SettingStore 的关系】
///   SettingStore 内部复用本方法，保证 AAD 派生规则完全一致；
///   业务代码优先走 SettingStore（顺带处理版本、审计、缓存），
///   本方法是"SettingStore 覆盖不到的场景"（如独立表的敏感字段）的出口。
/// </summary>
public static class CryptoSecretExtensions
{
    /// <summary>
    /// 敏感字段密文的版本前缀分隔符。
    /// 加密产出形如 "v1:base64(cipher)"；解密按前缀版本号路由到对应主密钥。
    /// </summary>
    private const char VersionSeparator = ':';

    /// <summary>
    /// 判断字符串是否为本系统加密产出的密文格式。
    /// 
    /// 判定规则（严格前缀匹配）：
    ///   - null / 空串 → false
    ///   - 长度 &lt; 3 → false
    ///   - 首字符必须为 'v'，第二字符必须为数字，前 10 字符内必须包含 ':'
    /// 
    /// 注意：本方法只判断"是不是密文格式"，不判断密文是否可解密。
    ///      解密失败在 <see cref="TryUnprotectAsync"/> 中处理。
    /// </summary>
    /// <param name="factory">算法工厂（本方法不实际使用，仅作为宿主避免静态类污染全局）。</param>
    /// <param name="value">待判断的字符串。</param>
    /// <returns>true 表示看起来是本系统密文；false 表示历史明文或非法输入。</returns>
    public static bool IsProtected(this ICryptoProviderFactory factory, string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length < 3) return false;
        if (value[0] != 'v') return false;
        if (!char.IsDigit(value[1])) return false;

        // 版本号最多 4 位（v1 ~ v9999），分隔符必须在合理范围内
        var sepIndex = value.IndexOf(VersionSeparator);
        return sepIndex >= 2 && sepIndex <= 5;
    }

    /// <summary>
    /// 加密敏感字段明文。
    /// 
    /// 【行为】
    ///   1) 读 SecretField 策略（默认 SM4-GCM）；
    ///   2) 由 factory 取对应 Encryptor；
    ///   3) 以 "{group}.{key}" 为 AAD 加密，产出 "v{n}:base64(...)"。
    /// 
    /// 【异常】
    ///   - plaintext 为 null → 抛 ArgumentNullException；
    ///   - group / key 为空 → 抛 ArgumentException；
    ///   - 策略读取失败、算法未注册、主密钥损坏 → 全部向上抛，绝不静默。
    /// </summary>
    /// <param name="factory">算法工厂。</param>
    /// <param name="policy">策略服务，内部会读取 CryptoUsage.SecretField。</param>
    /// <param name="group">分组键（如 "storage" / "smtp"）。</param>
    /// <param name="key">字段键（如 "ftpPassword" / "smtpPassword"）。</param>
    /// <param name="plaintext">明文内容，禁止为 null（空串允许）。</param>
    /// <param name="logger">可选日志器，为空时使用 NullLogger 静默。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>形如 "v1:base64(...)" 的密文。</returns>
    public static async Task<string> ProtectAsync(
        this ICryptoProviderFactory factory,
        ICryptoPolicyService policy,
        string group,
        string key,
        string plaintext,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        // ---- 参数校验（加密路径严格要求） ----
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(plaintext);

        var log = logger ?? NullLogger.Instance;

        // ---- 1) 读策略 ----
        var snapshot = await policy.GetAsync(CryptoUsage.SecretField, ct).ConfigureAwait(false);

        // ---- 2) 取算法实现 ----
        var encryptor = factory.GetEncryptor(snapshot.Algorithm);

        // ---- 3) AAD 派生（统一规则，调用方无法覆盖） ----
        var aad = BuildAad(group, key);

        // ---- 4) 加密（失败向上抛） ----
        try
        {
            var cipher = encryptor.Encrypt(plaintext, aad);
            log.LogDebug("已加密敏感字段 {Group}.{Key}（算法 {Algorithm}）",
                group, key, snapshot.Algorithm);
            return cipher;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "加密敏感字段 {Group}.{Key} 失败（算法 {Algorithm}）",
                group, key, snapshot.Algorithm);
            throw;
        }
    }

    /// <summary>
    /// 解密敏感字段（尽力而为，不抛异常）。
    /// 
    /// 【行为】
    ///   - null / 空串 → 原样返回；
    ///   - 非 v{n}: 前缀（历史明文）→ 原样返回 + WARN 日志；
    ///   - v{n}: 前缀密文 → 解密；失败时返回 null + ERROR 日志。
    /// 
    /// 【异常】
    ///   策略读取失败、算法未注册 → 向上抛（系统级故障，不应吞）；
    ///   密文格式错误、主密钥不匹配、AAD 不匹配 → 捕获，返回 null。
    /// </summary>
    /// <param name="factory">算法工厂。</param>
    /// <param name="policy">策略服务。</param>
    /// <param name="group">分组键。</param>
    /// <param name="key">字段键。</param>
    /// <param name="cipher">可能是 null / 明文 / 密文的字符串。</param>
    /// <param name="logger">可选日志器。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解密后的明文；历史明文原样返回；解密失败返回 null。</returns>
    public static async Task<string?> TryUnprotectAsync(
        this ICryptoProviderFactory factory,
        ICryptoPolicyService policy,
        string group,
        string key,
        string? cipher,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        // ---- 参数校验（解密路径宽松，null 直接原样返回） ----
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var log = logger ?? NullLogger.Instance;

        // ---- 情况 1：空输入 → 原样返回 ----
        if (string.IsNullOrEmpty(cipher)) return cipher;

        // ---- 情况 2：历史明文（非密文格式）→ 原样返回 + WARN ----
        if (!factory.IsProtected(cipher))
        {
            log.LogWarning(
                "敏感字段 {Group}.{Key} 存储的是历史明文（非 v{{n}}: 前缀），" +
                "已按原值返回；建议尽快通过配置页重写以完成加密迁移",
                group, key);
            return cipher;
        }

        // ---- 情况 3：密文 → 走解密 ----
        // 策略/工厂异常不捕获，向上传播（系统级故障）
        var snapshot = await policy.GetAsync(CryptoUsage.SecretField, ct).ConfigureAwait(false);
        var encryptor = factory.GetEncryptor(snapshot.Algorithm);
        var aad = BuildAad(group, key);

        try
        {
            var plain = encryptor.Decrypt(cipher, aad);
            return plain;
        }
        catch (Exception ex)
        {
            // 密文损坏、主密钥不匹配、AAD 不匹配等 → 降级为 null
            log.LogError(ex,
                "解密敏感字段 {Group}.{Key} 失败（算法 {Algorithm}）；" +
                "可能原因：主密钥丢失/被轮换、密文损坏、AAD 不匹配",
                group, key, snapshot.Algorithm);
            return null;
        }
    }

    /// <summary>
    /// 统一的 AAD 派生规则。
    /// 
    /// 采用 "{group}.{key}" 形式：
    ///   - 短、可读，便于排障；
    ///   - 与字段一一对应，跨字段复制密文会解密失败，天然防替换攻击。
    /// 
    /// 本方法**不对外暴露**，调用方无法传入自定义 AAD —— 保证全项目一致性。
    /// </summary>
    private static string BuildAad(string group, string key) => $"{group}.{key}";
}
