using Microsoft.Extensions.Logging;
using Station.Application.Security;
using Station.Contracts;
using Station.Data.IdGeneration;
using Station.Domain.Auditing;
using Station.Application.Auditing;
using Station.Domain.Licensing;
using Station.Domain.Clocks;
using System.ComponentModel.DataAnnotations;

namespace Station.Application.Licensing;

/// <summary>
/// 授权服务：
///   - 状态检查（DB + 时钟回拨检测）
///   - 激活流程（解析 → 加密校验 → 硬件校验 → 落库 → 审计）
/// 
/// 【依赖原则】
///   - 加密/签名/解密/验签全部走 ILicenseCryptoService 门面；
///   - 本服务不接触任何算法细节、密钥文件、策略。
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private readonly ILicenseRepository _licenses;
    private readonly IClockStateRepository _clockStates;
    private readonly LicenseOptions _options;
    private readonly IMachineFingerprintProvider _fingerprint;
    private readonly IIdGenerator _idGenerator;
    private readonly ILicenseCryptoService _licenseCrypto;
    private readonly IAuditLogService _audit;
    private readonly ILogger<LicenseService> _logger;

    public event EventHandler<LicenseCheckResult>? LicenseChanged;

    public LicenseService(
        ILicenseRepository licenses,
        IClockStateRepository clockStates,
        LicenseOptions options,
        IMachineFingerprintProvider fingerprint,
        IIdGenerator idGenerator,
        ILicenseCryptoService licenseCrypto,
        IAuditLogService audit,
        ILogger<LicenseService> logger)
    {
        _licenses = licenses;
        _clockStates = clockStates;
        _options = options;
        _fingerprint = fingerprint;
        _idGenerator = idGenerator;
        _licenseCrypto = licenseCrypto;
        _audit = audit;
        _logger = logger;
    }

    // =========================================================
    // 状态检查
    // =========================================================

    public async Task<LicenseCheckResult> CheckAsync()
    {
        // 1) 时钟回拨检测（防绕过到期）
        var rollback = await DetectClockRollbackAsync();
        if (rollback)
        {
            _logger.LogError("授权检查失败：检测到时钟回拨，授权已锁定");
            return new LicenseCheckResult(LicenseStatus.Deactivated, null, 0, false,
                "检测到时钟回拨，已锁定");
        }

        // 2) 查询当前激活授权
        var active = await _licenses.GetActiveAsync();
        if (active is null)
        {
            var trialEnd = DateTime.Now.AddDays(_options.TrialDays);
            return new LicenseCheckResult(LicenseStatus.Trial, trialEnd, _options.TrialDays, true,
                $"试用版（剩余 {_options.TrialDays} 天）");
        }

        // 3) 到期检查
        if (DateTime.Now > active.ExpiresAt)
        {
            _logger.LogWarning("授权已到期（{ExpiresAt}），请续期激活", active.ExpiresAt);
            return new LicenseCheckResult(LicenseStatus.Expired, active.ExpiresAt, 0, false,
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
        var state = await _clockStates.GetAsync();

        if (state is not null && state.LastCheckAt > now.AddMinutes(2))
            return true;

        await _clockStates.UpsertAsync(new ClockState
        {
            Id = 1,
            LastCheckAt = now,
            UpdatedAt = now
        });

        return false;
    }

    // =========================================================
    // 激活流程
    // =========================================================

    public async Task<(bool Ok, string Message)> ActivateAsync(string licenseFileText)
    {
        if (string.IsNullOrWhiteSpace(licenseFileText))
            return (false, "授权文件内容为空");

        // 1) 拿本机指纹（用于交叉校验）
        var currentFingerprint = _fingerprint.CollectFingerprint();

        // 2) 走门面完成验签 + 业务校验
        var validation = await _licenseCrypto.ValidateLicenseAsync(
            licenseFileText,
            expectedStationCode: null,       // 站点编号由内部 payload 校验；此处不再先 Parse
            expectedFingerprint: currentFingerprint);

        if (!validation.Valid)
        {
            _logger.LogWarning("授权校验失败：{Message}", validation.Message);
            return (false, validation.Message);
        }

        var payload = validation.Payload!;

        // 3) 停用旧授权
        var active = await _licenses.GetActiveAsync();
        active?.Revoke();
        await _licenses.UpdateAsync(active!);

        // 4) 落库（PayloadEnc 用 secret_field 策略加密）
        var protectedText = await _licenseCrypto.EncryptLicenseTextAsync(licenseFileText);
        var license = License.Create(_idGenerator.NewId(), payload.LicenseKey, payload.ProductCode, payload.StationCode, payload.Fingerprint, protectedText, payload.IssuedAt, payload.ExpiresAt, DateTime.Now);
        await _licenses.AddAsync(license);

        // 5) 审计
        await _audit.WriteAsync(new AuditLog
        {
            OperationType = "LicenseActivate",
            Target = "license",
            Detail = $"{{\"key\":\"{payload.LicenseKey}\"," +
                     $"\"expires\":\"{payload.ExpiresAt:yyyy-MM-dd}\"}}",
            Result = 1,
            CreatedAt = DateTime.UtcNow,
            SourceIp = "",
            SourceClient = "Desktop"
        });

        _logger.LogInformation("授权激活成功：{Key}（至 {ExpiresAt:yyyy-MM-dd}）",
            payload.LicenseKey, payload.ExpiresAt);

        LicenseChanged?.Invoke(this, await CheckAsync());
        return (true, "激活成功");
    }
}
