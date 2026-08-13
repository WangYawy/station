namespace Station.Application.Licensing;

/// <summary>授权服务：离线激活、状态检查、硬件绑定校验；到期影响采集。</summary>
public interface ILicenseService
{
    Task<LicenseCheckResult> CheckAsync();

    Task<(bool Ok, string Message)> ActivateAsync(string licenseFileText);

    Task<bool> IsValidNowAsync();
}
