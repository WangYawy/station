using Station.Contracts;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Licensing;
using Station.Infrastructure.Repositories;

namespace Station.Application.Licensing;

public sealed class LicenseService : ILicenseService
{
    private readonly IRepository<LicenseInfo> _licenses;
    private readonly LicenseOptions _options;
    private readonly IMachineFingerprintProvider _fingerprint;
    private readonly IIdGenerator _idGenerator;

    public LicenseService(
        IRepository<LicenseInfo> licenses,
        LicenseOptions options,
        IMachineFingerprintProvider fingerprint,
        IIdGenerator idGenerator)
    {
        _licenses = licenses;
        _options = options;
        _fingerprint = fingerprint;
        _idGenerator = idGenerator;
    }

    public async Task<LicenseCheckResult> CheckAsync()
    {
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
            return new LicenseCheckResult(LicenseStatus.Locked, active.ExpiresAt, 0, false,
                "授权已到期，请续期激活");
        }

        return new LicenseCheckResult(LicenseStatus.Activated, active.ExpiresAt, daysLeft, true,
            $"正式版（剩余 {daysLeft} 天）");
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

        if (!LicenseFileCodec.Verify(file, _options.PublicKeyPem))
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
            IssuedAt = file.IssuedAt,
            ExpiresAt = file.ExpiresAt,
            Status = LicenseStatus.Activated,
            ActivatedAt = DateTime.Now,
            IsActive = true
        });
        return (true, "激活成功");
    }

    public async Task<bool> IsValidNowAsync() =>
        (await CheckAsync()).IsValid;
}
