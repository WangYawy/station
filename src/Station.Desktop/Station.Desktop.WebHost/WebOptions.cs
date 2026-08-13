namespace Station.Desktop.WebHost;

/// <summary>单机内置 Web 配置，对应 <c>Station:Web</c>：默认仅本机访问，开启局域网后强制 HTTPS+认证+访问日志。</summary>
public sealed class WebOptions
{
    public const string SectionName = "Station:Web";

    /// <summary>本机监听地址（默认 127.0.0.1）。</summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 5000;

    /// <summary>开启局域网访问（监听 0.0.0.0；开启后强制 HTTPS 与访问日志）。</summary>
    public bool EnableLan { get; set; }

    public bool EnableHttps { get; set; }

    public int HttpsPort { get; set; } = 5443;

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }
}
