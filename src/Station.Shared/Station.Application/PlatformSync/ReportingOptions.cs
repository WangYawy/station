namespace Station.Application.PlatformSync;

/// <summary>上行上报签名配置，对应配置节 <c>Station:Reporting</c>。</summary>
public sealed class ReportingOptions
{
    public const string SectionName = "Station:Reporting";

    /// <summary>采集站上行上报 SM2 私钥（报警/授权状态签名；平台持对应公钥验签）。</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;
}
