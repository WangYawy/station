namespace Station.Domain.Settings;

/// <summary>运行时配置键值（POCO）。</summary>
public sealed class SysSetting
{
    public long Id { get; set; }
    public string GroupKey { get; set; } = string.Empty;
    public string SubKey { get; set; } = string.Empty;
    public string? ValueJson { get; set; }
    public string ValueType { get; set; } = "string";
    public bool IsEncrypted { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public string? Remark { get; set; }
}
