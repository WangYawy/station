namespace Station.Application.Security;

/// <summary>
/// 加密相关配置（对应 appsettings.json 节 Station:Crypto）。
/// </summary>
public sealed class CryptoOptions
{
    public const string SectionName = "Station:Crypto";

    /// <summary>主密钥文件路径（留空用平台默认路径）。</summary>
    public string MasterKeyFile { get; set; } = string.Empty;

    /// <summary>环境变量名（覆盖密钥文件）。</summary>
    public string EnvOverrideVar { get; set; } = "STATION_MASTER_KEY";
}
