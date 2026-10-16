using SqlSugar;

namespace Station.Infrastructure.Persistence.Settings;

[SugarTable("station_sys_setting")]
[SugarIndex("uk_setting_group_key", nameof(GroupKey), OrderByType.Asc,
            nameof(SubKey), OrderByType.Asc, true)]
public sealed class SysSettingPo
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(Length = 32)] public string GroupKey { get; set; } = "";
    [SugarColumn(Length = 64)] public string SubKey { get; set; } = "";
    [SugarColumn(ColumnDataType = "text", IsNullable = true)] public string? ValueJson { get; set; }
    [SugarColumn(Length = 16)] public string ValueType { get; set; } = "string";
    public bool IsEncrypted { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? UpdatedBy { get; set; }
    [SugarColumn(Length = 128, IsNullable = true)] public string? Remark { get; set; }
}
