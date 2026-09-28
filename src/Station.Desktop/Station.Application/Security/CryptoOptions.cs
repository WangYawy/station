namespace Station.Application.Security;

/// <summary>
/// 加密相关配置（对应 appsettings.json 节 Station:Crypto）。
/// </summary>
public sealed class CryptoOptions
{
    public const string SectionName = "Station:Crypto";

    /// <summary>
    /// 主密钥模式：
    ///   - "Local"：读本地文件（默认，单机部署）
    ///   - "Remote"：从管理平台拉取（集群部署，暂未实现）
    /// </summary>
    public string Mode { get; set; } = "Local";

    /// <summary>主密钥文件路径（Mode=Local 时使用；留空用平台默认路径）。</summary>
    public string MasterKeyFile { get; set; } = string.Empty;

    /// <summary>环境变量名（Mode=Local 时可选覆盖密钥文件）。</summary>
    public string EnvOverrideVar { get; set; } = "STATION_MASTER_KEY";

    // ---- Remote 模式配置（预留）----

    /// <summary>管理平台地址（Mode=Remote 时使用）。</summary>
    public string RemoteEndpoint { get; set; } = string.Empty;

    /// <summary>节点身份标识（Mode=Remote 时使用，默认取机器指纹）。</summary>
    public string NodeId { get; set; } = string.Empty;
}
