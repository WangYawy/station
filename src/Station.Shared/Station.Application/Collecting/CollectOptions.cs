namespace Station.Application.Collecting;

/// <summary>
/// 采集策略配置，对应配置节 <c>Station:Collect</c>。
/// 默认：接入即自动采集、全量、跳过已采集、擦除关闭（需二次确认）。
/// </summary>
public sealed class CollectOptions
{
    public const string SectionName = "Station:Collect";

    /// <summary>采集本地缓存目录（待上传中间态）。</summary>
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Station", "collect-cache");

    /// <summary>模拟记录仪根目录（开发用；空则默认 CacheDirectory/sim-recorder）。</summary>
    public string? SimulatedSourceDirectory { get; set; }

    /// <summary>采集源模式：simulated（开发默认）/ ums（真实U盘/记录仪，UMS 协议）。</summary>
    public string SourceMode { get; set; } = "simulated";

    /// <summary>UMS 根目录覆盖（开发/测试用；为空时自动枚举可移动磁盘）。</summary>
    public string? UmsRootOverride { get; set; }

    /// <summary>记录仪接入后是否自动开始采集（需求：默认自动）。</summary>
    public bool AutoCollectOnConnect { get; set; } = true;

    /// <summary>已采集过的文件自动跳过（按指纹）。</summary>
    public bool SkipCollected { get; set; } = true;

    /// <summary>任务完成后自动擦除记录仪上已采集文件（需求：策略默认关闭）。</summary>
    public bool EraseAfterComplete { get; set; }

    public int ChunkBytes { get; set; } = 1024 * 1024;

    /// <summary>模拟源每分块耗时（毫秒），用于演示可见进度与可靠的暂停/中断测试。</summary>
    public int SimulatedChunkDelayMs { get; set; } = 15;

    /// <summary>采集文件扩展名白名单。</summary>
    public List<string> FileExtensions { get; set; } =
    [
        ".mp4", ".avi", ".flv", ".mov",
        ".wav", ".mp3", ".aac",
        ".jpg", ".bmp", ".jpeg", ".png",
        ".log"
    ];

    /// <summary>模拟记录仪生成的文件数（开发验证用）。</summary>
    public int SimulatedFileCount { get; set; } = 5;
}
