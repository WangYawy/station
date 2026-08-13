namespace Station.Application.PlatformSync;

/// <summary>指令签名校验配置，对应配置节 <c>Station:Command</c>。</summary>
public sealed class CommandVerifierOptions
{
    public const string SectionName = "Station:Command";

    /// <summary>采集站内置 SM2 公钥（与平台下发私钥配对）。</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>是否强制验签（生产必须 true；未配置公钥时 false 可跳过以兼容旧部署）。</summary>
    public bool Required { get; set; } = false;
}
