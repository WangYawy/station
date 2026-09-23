namespace Station.Application.PlatformSync;

/// <summary>指令签名校验配置，对应配置节 <c>Station:Command</c>。</summary>
public sealed class CommandVerifierOptions
{
    public const string SectionName = "Station:Command";
    public string SignAlgorithm { get; set; } = "SM2-SM3";
    public string PublicKeyFile { get; set; } = string.Empty;
    public bool Required { get; set; } = false;
}
