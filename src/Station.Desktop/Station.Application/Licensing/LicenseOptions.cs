namespace Station.Application.Licensing;

/// <summary>授权配置，对应配置节 <c>Station:License</c>。</summary>
public sealed class LicenseOptions
{
    public const string SectionName = "Station:License";

    /// <summary>采集站内置 SM2 公钥（验签；生产由部署方配置，与内部工具私钥配对）。</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>内部工具私钥（仅生成工具持有；采集站不配置）。</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>未激活时的试用天数。</summary>
    public int TrialDays { get; set; } = 30;

    public string ProductCode { get; set; } = "STATION-DESKTOP-1";
}
