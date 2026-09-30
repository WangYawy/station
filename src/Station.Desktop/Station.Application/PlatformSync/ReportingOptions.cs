namespace Station.Application.PlatformSync;

/// <summary>上行上报签名配置，对应配置节 <c>Station:Reporting</c>。</summary>
public sealed class ReportingOptions
{
    public const string SectionName = "Station:Reporting";
    public string SignAlgorithm { get; set; } = "SM2-SM3";
    public string PrivateKeyFile { get; set; } = "keys/reporting-private.pem";
    public string PublicKeyFile { get; set; } = "keys/reporting-public.pem";
}
