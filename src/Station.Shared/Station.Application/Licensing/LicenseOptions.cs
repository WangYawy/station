namespace Station.Application.Licensing;

/// <summary>授权配置，对应配置节 <c>Station:License</c>。</summary>
public sealed class LicenseOptions
{
    public const string SectionName = "Station:License";

    /// <summary>授权文件签名密钥（内部工具与采集站共用；生产应替换并保密）。</summary>
    public string SigningKey { get; set; } = "station-license-signing-key-v1";

    /// <summary>未激活时的试用天数。</summary>
    public int TrialDays { get; set; } = 30;

    public string ProductCode { get; set; } = "STATION-DESKTOP-1";
}
