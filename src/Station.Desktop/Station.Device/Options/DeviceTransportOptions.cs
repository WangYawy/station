namespace Station.Device.Options;

/// <summary>设备传输配置。</summary>
public sealed class DeviceTransportOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Station:DeviceTransport";

    /// <summary>采集缓存目录（本地落盘位置）。</summary>
    public string CacheDirectory { get; set; } = "Cache";
}
