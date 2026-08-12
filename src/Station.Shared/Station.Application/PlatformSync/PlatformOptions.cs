namespace Station.Application.PlatformSync;

/// <summary>平台通信配置，对应配置节 <c>Station:Platform</c>。</summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Station:Platform";

    /// <summary>平台服务地址（平台版必填）。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>采集站编号（注册与 FileNo 前缀）。</summary>
    public string StationCode { get; set; } = "ST0001";

    /// <summary>上报/补报轮询间隔（秒）。</summary>
    public int SyncIntervalSeconds { get; set; } = 10;

    /// <summary>指令轮询间隔（秒），独立于上报。</summary>
    public int CommandPollIntervalSeconds { get; set; } = 10;

    /// <summary>HTTP 超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 30;

    public bool Enabled { get; set; }
}
