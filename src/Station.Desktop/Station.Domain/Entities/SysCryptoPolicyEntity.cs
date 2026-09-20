using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 加密算法策略表：一个"用途"一行，便于扩展与审计
/// </summary>
[SugarTable("station_crypto_policy")]
public class SysCryptoPolicy
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    public string UsageCode { get; set; } = string.Empty;      // password / license / file_sig / secret_field / transport
    public string Algorithm { get; set; } = string.Empty;     // MD5 / SM3 / SM4-GCM / SM2-SM3 / PBKDF2-SHA256 / BCrypt
    public string SecondaryAlgorithm { get; set; } = string.Empty; // 组合算法（如 SM4+SM3 加密并签名）
    public string ParamsJson { get; set; } = string.Empty;     // {iterations:100000, saltLen:16, keySize:128, mode:"GCM"}
    public bool AllowLegacy { get; set; } = true; // 是否接受旧算法数据（迁移期用）
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}
