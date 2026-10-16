namespace Station.Application.UsbPortCards;

/// <summary>端口映射配置。</summary>
public sealed class UsbPortMapOptions
{
    public const string SectionName = "Station:UsbPortMap";

    /// <summary>是否启用映射。</summary>
    public bool Enable { get; set; } = true;

    /// <summary>布局：RowMajor / ColumnMajor（仅语义）。</summary>
    public string Layout { get; set; } = "RowMajor";

    /// <summary>物理端口 ID → 1-30 序号。</summary>
    public Dictionary<string, int> Mapping { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
