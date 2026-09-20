namespace Station.Application.Licensing;

/// <summary>授权服务：离线激活、状态检查、硬件绑定校验；到期影响采集。</summary>
public interface ILicenseService
{
    /// <summary>License 状态发生变化（激活、导入、到期、试用天数跨天等）时触发。</summary>
    event EventHandler<LicenseCheckResult>? LicenseChanged;

    Task<LicenseCheckResult> CheckAsync();

    Task<(bool Ok, string Message)> ActivateAsync(string licenseFileText);

    Task<bool> IsValidNowAsync();
}
