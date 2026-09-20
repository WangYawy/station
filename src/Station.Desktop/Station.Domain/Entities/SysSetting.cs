using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 通用键值设置表：替代 IConfiguration 之外的运行时配置
/// </summary>
[SugarTable("station_sys_setting")]
public class SysSetting
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    public string GroupKey { get; set; } = string.Empty;       // basic / storage / collect / security / crypto
    public string SubKey { get; set; } = string.Empty;         // 组内细分，如 "password" / "file_sig"
    public string ValueJson { get; set; } = string.Empty;     // 值（JSON 序列化）
    public string ValueType { get; set; } = string.Empty;     // string/int/bool/json/secret
    public bool IsEncrypted { get; set; }      // secret=true，读时解密
    public int Version { get; set; } = 1;      // 乐观锁 + 历史版本
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    [SugarColumn(IsIgnore = true)] public string RowVersion { get; set; } = string.Empty;
}
