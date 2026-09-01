using Station.Contracts;
using Station.Domain.Entities;
using Station.Application.IdGenerators;
using Station.Infrastructure.Licensing;
using Station.Domain.Repositories;
using Station.Domain.Security;
using Microsoft.Extensions.Logging;

namespace Station.Application.Licensing;

public sealed class LicenseService : ILicenseService
{
    private readonly IRepository<LicenseInfo> _licenses;
    private readonly IRepository<ClockState> _clockStates;
    private readonly LicenseOptions _options;
    private readonly IMachineFingerprintProvider _fingerprint;
    private readonly IIdGenerator _idGenerator;
    private readonly ILogger<LicenseService> _logger;
    private readonly ILicenseSignatureService _licenseSignature;
    private readonly ISecretProtector _secretProtector;

    public LicenseService(
        IRepository<LicenseInfo> licenses,
        IRepository<ClockState> clockStates,
        LicenseOptions options,
        IMachineFingerprintProvider fingerprint,
        IIdGenerator idGenerator,
        ILicenseSignatureService licenseSignature,
        ISecretProtector secretProtector,
        ILogger<LicenseService> logger)
    {
        _licenses = licenses;
        _clockStates = clockStates;
        _options = options;
        _fingerprint = fingerprint;
        _idGenerator = idGenerator;
        _licenseSignature = licenseSignature;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task<LicenseCheckResult> CheckAsync()
    {
        var rollback = await DetectClockRollbackAsync();
        if (rollback)
        {
            _logger.LogError("授权检查失败：检测到时钟回拨，授权已锁定");
            return new LicenseCheckResult(LicenseStatus.Locked, null, 0, false, "检测到时钟回拨，已锁定");
        }

        var active = await _licenses.FirstAsync(l => l.IsActive);
        if (active is null)
        {
            var trialEnd = DateTime.Now.AddDays(_options.TrialDays);
            return new LicenseCheckResult(LicenseStatus.Trial, trialEnd, _options.TrialDays, true,
                $"试用版（剩余 {_options.TrialDays} 天）");
        }

        var daysLeft = Math.Max(0, (int)(active.ExpiresAt - DateTime.Now).TotalDays);
        if (DateTime.Now > active.ExpiresAt)
        {
            _logger.LogWarning("授权已到期（{ExpiresAt}），请续期激活", active.ExpiresAt);
            return new LicenseCheckResult(LicenseStatus.Locked, active.ExpiresAt, 0, false,
                "授权已到期，请续期激活");
        }

        return new LicenseCheckResult(LicenseStatus.Activated, active.ExpiresAt, daysLeft, true,
            $"正式版（剩余 {daysLeft} 天）");
    }

    /// <summary>时钟回拨检测：最近一次检查时间晚于当前时间（容差 2 分钟）视为回拨。</summary>
    private async Task<bool> DetectClockRollbackAsync()
    {
        var now = DateTime.Now;
        var state = await _clockStates.FirstAsync(c => c.Id == 1);
        if (state is not null && state.LastCheckAt > now.AddMinutes(2))
        {
            return true;
        }

        if (state is null)
        {
            await _clockStates.InsertAsync(new ClockState { Id = 1, LastCheckAt = now, UpdatedAt = now });
        }
        else
        {
            state.LastCheckAt = now;
            state.UpdatedAt = now;
            await _clockStates.UpdateAsync(state);
        }

        return false;
    }

    public async Task<(bool Ok, string Message)> ActivateAsync(string licenseFileText)
    {
        LicenseFile file;
        try
        {
            file = LicenseFileCodec.Parse(licenseFileText);
        }
        catch (Exception ex)
        {
            return (false, $"授权文件无效：{ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(_options.PublicKeyPem))
        {
            return (false, "未配置授权公钥（无法验签）");
        }

        if (_licenseSignature.Verify(_options.PublicKeyPem, LicenseFileCodec.Canonical(file), file.Signature))
        // if (!LicenseFileCodec.Verify(file, _options.PublicKeyPem))
        {
            return (false, "授权文件签名无效");
        }

        if (file.Fingerprint != _fingerprint.CollectFingerprint())
        {
            return (false, "授权与本机硬件指纹不匹配（换硬件需重新授权）");
        }

        if (file.ExpiresAt <= DateTime.Now)
        {
            return (false, "授权已过期，无法激活");
        }

        // 一台机器只认最新授权：新激活使所有旧授权失效
        var actives = await _licenses.GetListAsync(l => l.IsActive);
        foreach (var existing in actives)
        {
            existing.IsActive = false;
        }

        if (actives.Count > 0)
        {
            await _licenses.UpdateRangeAsync(actives);
        }

        await _licenses.InsertAsync(new LicenseInfo
        {
            Id = _idGenerator.NextId(),
            LicenseKey = file.LicenseKey,
            ProductCode = file.ProductCode,
            StationCode = file.StationCode,
            Fingerprint = file.Fingerprint,
            PayloadEnc = _secretProtector.Protect(licenseFileText),
            IssuedAt = file.IssuedAt,
            ExpiresAt = file.ExpiresAt,
            Status = LicenseStatus.Activated,
            ActivatedAt = DateTime.Now,
            IsActive = true
        });
        _logger.LogInformation("授权激活成功：{Key}（至 {ExpiresAt:yyyy-MM-dd}）", file.LicenseKey, file.ExpiresAt);
        return (true, "激活成功");
    }

    public async Task<bool> IsValidNowAsync() =>
        (await CheckAsync()).IsValid;
}
