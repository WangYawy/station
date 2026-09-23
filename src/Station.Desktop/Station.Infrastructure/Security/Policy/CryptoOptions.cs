namespace Station.Infrastructure.Security.Policy;

/// <summary>
/// 加密相关配置，对应 appsettings.json 节 <c>Station:Crypto</c>。
/// </summary>
public sealed class CryptoOptions
{
    public const string SectionName = "Station:Crypto";

    /// <summary>主密钥文件路径。留空则用平台默认。</summary>
    public string MasterKeyFile { get; set; } = string.Empty;

    /// <summary>环境变量名（覆盖密钥文件）。</summary>
    public string EnvOverrideVar { get; set; } = "STATION_MASTER_KEY";

    /// <summary>出厂默认算法（DB 策略缺失时兜底）。</summary>
    public Dictionary<string, string> FallbackDefaults { get; set; } = new();
}
