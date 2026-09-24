using Microsoft.Extensions.Logging;
using Station.Application.Audit;
using Station.Application.IdGenerators;
using Station.Application.Security;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Repositories;

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
    private readonly IRepository<LicenseInfo> _licenses;
    private readonly IRepository<ClockState> _clockStates;
    private readonly LicenseOptions _options;
    private readonly IMachineFingerprintProvider _fingerprint;
    private readonly IIdGenerator _idGenerator;
    private readonly ILicenseCryptoService _licenseCrypto;
    private readonly IAuditLogService _audit;
    private readonly ILogger<LicenseService> _logger;

    public event EventHandler<LicenseCheckResult>? LicenseChanged;

    public LicenseService(
        IRepository<LicenseInfo> licenses,
        IRepository<ClockState> clockStates,
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
            return new LicenseCheckResult(LicenseStatus.Locked, null, 0, false,
                "检测到时钟回拨，已锁定");
        }

        // 2) 查询当前激活授权
        var active = await _licenses.FirstAsync(l => l.IsActive);
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

    // =========================================================
    // 激活流程
    // =========================================================

    public async Task<(bool Ok, string Message)> ActivateAsync(string licenseFileText)
    {
        if (string.IsNullOrWhiteSpace(licenseFileText))
            return (false, "授权文件内容为空");

        // ---- 1) 解析文件结构（快速失败，避免后续无意义处理）----
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

        // ---- 2) 走 ILicenseCryptoService 完成验签 + 解密 + 站点/指纹/到期校验 ----
        var currentFingerprint = _fingerprint.CollectFingerprint();
        var validation = await _licenseCrypto.ValidateLicenseAsync(
            licenseFileText,
            expectedStationCode: file.StationCode,       // 用文件里声明的站点号做交叉验证
            expectedFingerprint: currentFingerprint);

        if (!validation.Valid)
        {
            _logger.LogWarning("授权校验失败：{Message}", validation.Message);
            return (false, validation.Message);
        }

        var payload = validation.Payload!;

        // ---- 3) 停用旧授权（一机只认最新）----
        var actives = await _licenses.GetListAsync(l => l.IsActive);
        foreach (var existing in actives) existing.IsActive = false;
        if (actives.Count > 0) await _licenses.UpdateRangeAsync(actives);

        // ---- 4) 落库（PayloadEnc 用 secret_field 策略加密）----
        var protectedText = await _licenseCrypto.EncryptLicenseTextAsync(licenseFileText);

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

        // ---- 5) 审计 ----
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
}
