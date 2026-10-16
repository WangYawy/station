namespace Station.Device.Options;

/// <summary>设备检测配置。</summary>
public sealed class DeviceDetectionOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Station:DeviceDetection";

    /// <summary>检测源模式。默认 <see cref="SourceMode.Mixed"/>。</summary>
    public SourceMode SourceMode { get; set; } = SourceMode.Mixed;

    /// <summary>轮询间隔秒。默认 3。</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>稳定判定次数（连续出现 N 次判定接入）。默认 2。</summary>
    public int SettlePolls { get; set; } = 2;

    /// <summary>UMS 根路径覆盖（开发 / 测试用）；为 null 时按真实盘符。</summary>
    public string? UmsRootOverride { get; set; }
}
