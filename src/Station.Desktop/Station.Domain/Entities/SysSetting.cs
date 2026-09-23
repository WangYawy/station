using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>
/// 运行时配置键值表（覆盖 appsettings.json 中可动态修改的项）。
/// 敏感字段（如 FTP 密码）以 IsEncrypted=1 标记，ValueJson 存储 base64 密文。
/// </summary>
[SugarTable("station_sys_setting")]
[SugarIndex("uk_setting_group_key", nameof(GroupKey), OrderByType.Asc,
            nameof(SubKey), OrderByType.Asc, true)]
public sealed class SysSetting
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    /// <summary>分组键：basic / storage / collect / workbench / network / crypto。</summary>
    [SugarColumn(Length = 32)]
    public string GroupKey { get; set; } = string.Empty;

    /// <summary>组内子键：字段名或 usageCode。</summary>
    [SugarColumn(Length = 64)]
    public string SubKey { get; set; } = string.Empty;

    /// <summary>值（JSON 序列化；IsEncrypted=1 时为 base64 密文，带 v{n}: 前缀）。</summary>
    [SugarColumn(ColumnDataType = "text", IsNullable = true)]
    public string? ValueJson { get; set; }

    /// <summary>值类型：string / int / bool / json / secret。</summary>
    [SugarColumn(Length = 16)]
    public string ValueType { get; set; } = "string";

    /// <summary>是否加密存储（true 时 ValueJson 为密文，读时用 secret_field 策略解密）。</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>版本号（乐观锁）。每次更新 +1。</summary>
    public int Version { get; set; } = 1;

    public DateTime UpdatedAt { get; set; }

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? UpdatedBy { get; set; }

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? Remark { get; set; }
}
