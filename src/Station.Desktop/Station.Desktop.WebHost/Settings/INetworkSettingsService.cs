namespace Station.Desktop.WebHost.Settings;

/// <summary>网络安全设置（Web 端口/HTTPS/局域网/登录锁定）。</summary>
public sealed record NetworkSettingsDto(
    int WebPort,
    int HttpsPort,
    bool EnableLan,
    bool EnableHttps,
    bool AllowHttp,
    string CertificateStatus,
    int MaxFailedAttempts,
    int LockoutMinutes);

public interface INetworkSettingsService
{
    NetworkSettingsDto Get();

    /// <summary>更新网络安全设置；返回需要重启生效的提示。</summary>
    IReadOnlyList<string> Update(IReadOnlyDictionary<string, string> values);

    /// <summary>上传 HTTPS 证书（PFX，base64）；成功后启用 HTTPS。</summary>
    (bool Ok, string Message) UploadCertificate(string fileName, string base64Content, string password);
}
