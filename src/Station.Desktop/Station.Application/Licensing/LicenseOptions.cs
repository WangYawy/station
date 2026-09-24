namespace Station.Application.Licensing;

/// <summary>
/// 授权配置，对应配置节 <c>Station:License</c>。
/// 
/// 说明：
///   - 算法字段为"兜底默认"，实际运行以 station_crypto_policy 表为准；
///   - 密钥统一用文件路径，不再内嵌 PEM。
/// </summary>
public sealed class LicenseOptions
{
    public const string SectionName = "Station:License";

    /// <summary>验签算法（兜底默认）。新格式授权文件自带 Algo 字段，优先生效。</summary>
    public string SignAlgorithm { get; set; } = "SM2-SM3";

    /// <summary>内容加密算法（兜底默认）。</summary>
    public string EncryptAlgorithm { get; set; } = "SM4-GCM";

    /// <summary>公钥文件路径（相对应用目录或绝对路径）。</summary>
    public string PublicKeyFile { get; set; } = "keys/license-public.pem";

    /// <summary>私钥文件路径。仅内部授权工具使用；采集站留空。</summary>
    public string PrivateKeyFile { get; set; } = string.Empty;

    /// <summary>未激活时的试用天数。</summary>
    public int TrialDays { get; set; } = 30;

    /// <summary>产品编号。</summary>
    public string ProductCode { get; set; } = "STATION-DESKTOP-1";
}
