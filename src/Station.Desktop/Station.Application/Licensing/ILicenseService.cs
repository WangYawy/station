namespace Station.Application.Licensing;

/// <summary>
/// 授权服务：离线激活、状态检查、硬件绑定校验；到期影响采集。
/// 
/// 【职责边界】
///   - 本服务只处理业务逻辑（DB、审计、时钟回拨、硬件校验）；
///   - 授权文件的加密/签名/解密/验签全部委托 <see cref="Security.ILicenseCryptoService"/>。
/// </summary>
public interface ILicenseService
{
    /// <summary>License 状态发生变化（激活、导入、到期、试用天数跨天等）时触发。</summary>
    event EventHandler<LicenseCheckResult>? LicenseChanged;

    /// <summary>检查当前授权状态。</summary>
    Task<LicenseCheckResult> CheckAsync();

    /// <summary>激活授权（解析文件 → 加密校验 → 硬件校验 → 落库）。</summary>
    Task<(bool Ok, string Message)> ActivateAsync(string licenseFileText);

    /// <summary>当前授权是否有效（简写）。</summary>
    Task<bool> IsValidNowAsync();
}
