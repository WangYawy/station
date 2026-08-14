namespace Station.Application.Settings;

/// <summary>
/// 采集站基本设置，对应配置节 <c>Station:Basic</c>（安装位置、本地保留天数等）。
/// 本机编号沿用 <c>StorageOptions.StationNo</c>，运行模式沿用 <c>PlatformOptions.Enabled</c>。
/// </summary>
public sealed class StationOptions
{
    public const string SectionName = "Station:Basic";

    /// <summary>安装位置（如"一楼大厅东侧"），仅本地展示。</summary>
    public string InstallLocation { get; set; } = string.Empty;

    /// <summary>视频缓存保留天数（本地加密缓存清理）。</summary>
    public int VideoRetentionDays { get; set; } = 90;

    /// <summary>日志/审计保留天数。</summary>
    public int LogRetentionDays { get; set; } = 180;
}
