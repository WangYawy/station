using System.Text;
using Microsoft.Extensions.Logging;
using Station.Application.Audit;
using Station.Application.IdGenerators;
using Station.Application.Security;
using Station.Application.Security.Abstractions;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Repositories;
using Station.Domain.Security;
using Station.Infrastructure.Licensing;

namespace Station.Application.Licensing;

/// <summary>
/// 授权服务：
///   - 生成 / 校验 / 激活授权文件；
///   - 授权文本落库时用 secret_field 策略加密存储；
///   - 算法切换走 ICryptoPolicyService + ICryptoProviderFactory；
///   - 密钥从 PEM 文件读取（IKeyFileResolver），不走 appsettings；
///   - 所有关键动作写审计。
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private readonly IRepository<LicenseInfo> _licenses;
    private readonly IRepository<ClockState> _clockStates;
    private readonly LicenseOptions _options;
    private readonly IMachineFingerprintProvider _fingerprint;
    private readonly IIdGenerator _idGenerator;

    private readonly ICryptoPolicyService _cryptoPolicy;
    private readonly ICryptoProviderFactory _cryptoFactory;
    private readonly IKeyFileResolver _keyResolver;
    private readonly IAuditLogService _audit;

    private readonly ILogger<LicenseService> _logger;

    public event EventHandler<LicenseCheckResult>? LicenseChanged;

    public LicenseService(
        IRepository<LicenseInfo> licenses,
        IRepository<ClockState> clockStates,
        LicenseOptions options,
        IMachineFingerprintProvider fingerprint,
        IIdGenerator idGenerator,
        ICryptoPolicyService cryptoPolicy,
        ICryptoProviderFactory cryptoFactory,
        IKeyFileResolver keyResolver,
        IAuditLogService audit,
        ILogger<LicenseService> logger)
    {
        _licenses = licenses;
        _clockStates = clockStates;
        _options = options;
        _fingerprint = fingerprint;
        _idGenerator = idGenerator;
        _cryptoPolicy = cryptoPolicy;
        _cryptoFactory = cryptoFactory;
        _keyResolver = keyResolver;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// 授权状态检查
    /// </summary>
    /// <returns></returns>
    public async Task<LicenseCheckResult> CheckAsync()
    {
        var rollback = await DetectClockRollbackAsync();
        if (rollback)
        {
            _logger.LogError("授权检查失败：检测到时钟回拨，授权已锁定");
            return new LicenseCheckResult(LicenseStatus.Locked, null, 0, false,
                "检测到时钟回拨，已锁定");
        }

        var active = await _licenses.FirstAsync(l => l.IsActive);
        if (active is null)
        {
            var trialEnd = DateTime.Now.AddDays(_options.TrialDays);
            return new LicenseCheckResult(LicenseStatus.Trial, trialEnd, _options.TrialDays, true,
                $"试用版（剩余 {_options.TrialDays} 天）");
        }

        if (DateTime.Now > active.ExpiresAt)
        {
            _logger.LogWarning("授权已到期（{ExpiresAt}），请续期激活", active.ExpiresAt);
            return new LicenseCheckResult(LicenseStatus.Locked, active.ExpiresAt, 0, false,
                "授权已到期，请续期激活");
        }

        var daysLeft = Math.Max(0, (int)(active.ExpiresAt - DateTime.Now).TotalDays);
        return new LicenseCheckResult(LicenseStatus.Activated, active.ExpiresAt, daysLeft, true,
            $"正式版（剩余 {daysLeft} 天）");
    }

    public async Task<bool> IsValidNowAsync() => (await CheckAsync()).IsValid;

    /// <summary>时钟回拨检测：最近一次检查时间晚于当前时间（容差 2 分钟）视为回拨。</summary>
    private async Task<bool> DetectClockRollbackAsync()
    {
        var now = DateTime.Now;
        var state = await _clockStates.FirstAsync(c => c.Id == 1);

        if (state is not null && state.LastCheckAt > now.AddMinutes(2))
            return true;

        if (state is null)
        {
            await _clockStates.InsertAsync(new ClockState
            {
                Id = 1,
                LastCheckAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            state.LastCheckAt = now;
            state.UpdatedAt = now;
            await _clockStates.UpdateAsync(state);
        }

        return false;
    }

    /// <summary>
    /// 激活流程
    /// </summary>
    /// <param name="licenseFileText"></param>
    /// <returns></returns>
    public async Task<(bool Ok, string Message)> ActivateAsync(string licenseFileText)
    {
        if (string.IsNullOrWhiteSpace(licenseFileText))
            return (false, "授权文件内容为空");

        // ---- 1) 解析文件结构 ----
        LicenseFile file;
        try
        {
            file = LicenseFileCodec.Parse(licenseFileText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "授权文件解析失败");
            return (false, $"授权文件无效：{ex.Message}");
        }

        // ---- 2) 读加密策略（决定签名/加密算法） ----
        var policy = await _cryptoPolicy.GetAsync(CryptoUsage.License);

        // ---- 3) 读公钥文件 ----
        var publicKeyPem = await _keyResolver.ResolveAsync(_options.PublicKeyFile);
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return (false, $"未配置授权公钥文件：{_options.PublicKeyFile}");

        // ---- 4) 解析算法标识 ----
        // 新格式优先用文件内 Algo；旧格式回退到配置默认。
        var (encryptAlgo, signAlgo) = ParseAlgoTag(
            file.Algo,
            fallbackEncrypt: _options.EncryptAlgorithm,
            fallbackSign: _options.SignAlgorithm);

        // ---- 5) 验签 ----
        // 新格式：对 PayloadCipher 密文验签
        var signer = _cryptoFactory.GetSigner(signAlgo);
        var dataToVerify = Encoding.UTF8.GetBytes(file.PayloadCipher);

        if (!signer.Verify(dataToVerify, file.Signature, publicKeyPem))
        {
            _logger.LogWarning("授权文件验签失败（算法 {Algo}）", signAlgo);
            return (false, "授权文件签名无效");
        }

        // ---- 6) 解密内容（仅新格式） ----
        LicenseFile payload = file;

        try
        {
            var encryptor = _cryptoFactory.GetEncryptor(encryptAlgo);
            var plainJson = encryptor.Decrypt(file.PayloadCipher, aad: "license.payload");
            payload = LicenseFileCodec.Parse(plainJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "授权内容解密失败");
            return (false, "授权内容解密失败（密钥不匹配或文件被篡改）");
        }


        // ---- 7) 业务校验 ----
        var currentFp = _fingerprint.CollectFingerprint();
        if (!string.Equals(payload.Fingerprint, currentFp, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("授权指纹不匹配：文件 {F1}，本机 {F2}", payload.Fingerprint, currentFp);
            return (false, "授权与本机硬件指纹不匹配（换硬件需重新授权）");
        }

        if (payload.ExpiresAt <= DateTime.Now)
            return (false, "授权已过期，无法激活");

        // ---- 8) 停用旧授权（一机只认最新） ----
        var actives = await _licenses.GetListAsync(l => l.IsActive);
        foreach (var existing in actives) existing.IsActive = false;
        if (actives.Count > 0) await _licenses.UpdateRangeAsync(actives);

        // ---- 9) 落库（PayloadEnc 用 secret_field 策略加密） ----
        var protectedText = await _cryptoFactory.ProtectAsync(_cryptoPolicy, "license", "payload", licenseFileText, _logger);

        await _licenses.InsertAsync(new LicenseInfo
        {
            Id = _idGenerator.NextId(),
            LicenseKey = payload.LicenseKey,
            ProductCode = payload.ProductCode,
            StationCode = payload.StationCode,
            Fingerprint = payload.Fingerprint,
            PayloadEnc = protectedText,
            IssuedAt = payload.IssuedAt,
            ExpiresAt = payload.ExpiresAt,
            Status = LicenseStatus.Activated,
            ActivatedAt = DateTime.Now,
            IsActive = true
        });

        // ---- 10) 审计 ----
        await _audit.WriteAsync(new AuditLog
        {
            OperationType = "LicenseActivate",
            Target = "license",
            Detail = $"{{\"key\":\"{payload.LicenseKey}\"," +
                     $"\"expires\":\"{payload.ExpiresAt:yyyy-MM-dd}\"," +
                     $"\"algo\":\"{file.Algo}\"}}",
            Result = 1,
            CreatedAt = DateTime.UtcNow,
            ClientInfo = "Desktop"
        });

        _logger.LogInformation("授权激活成功：{Key}（至 {ExpiresAt:yyyy-MM-dd}）",
            payload.LicenseKey, payload.ExpiresAt);

        // 通知订阅者（如 UI 刷新状态）
        LicenseChanged?.Invoke(this, await CheckAsync());

        return (true, "激活成功");
    }

    /// <summary>
    /// 解析 "加密+签名" 复合标识（例如 "SM4-GCM+SM2-SM3"）。
    /// 缺失时回退到配置默认。
    /// </summary>
    private static (string Encrypt, string Sign) ParseAlgoTag(
        string? tag, string fallbackEncrypt, string fallbackSign)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return (fallbackEncrypt, fallbackSign);

        var idx = tag.IndexOf('+');
        if (idx < 0)
            return (tag.Trim(), fallbackSign);

        return (tag[..idx].Trim(), tag[(idx + 1)..].Trim());
    }
}
