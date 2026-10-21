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

    /// <summary>
    /// 采集源模式：simulated（开发默认）/ ums|mtp（真实设备模式）。
    /// 真实模式下按设备实际协议逐台路由（UMS/MTP 可混合接入）。
    /// </summary>
    public string SourceMode { get; set; } = ""; // simulated

    /// <summary>UMS 根目录覆盖（开发/测试用；为空时自动枚举可移动磁盘）。</summary>
    public string? UmsRootOverride { get; set; }

    /// <summary>MTP 设备过滤（友好名称或 PnP ID 包含该串；为空时取第一个 MTP 设备）。</summary>
    public string? MtpDeviceFilter { get; set; }

    /// <summary>本地缓存文件加密（需求：文件缓存使用 SM4 加密；默认开启）。</summary>
    public bool EncryptCache { get; set; } = true;

    /// <summary>本地加密缓存保留天数（需求默认 30 天），超期自动清理并留审计。</summary>
    public int CacheRetentionDays { get; set; } = 30;

    /// <summary>定时采集：启用后在每日指定时间自动开始采集（需记录仪已连接）。</summary>
    public bool ScheduledCollectEnabled { get; set; }

    public int ScheduleHour { get; set; } = 2;

    public int ScheduleMinute { get; set; } = 0;

    public int CacheCleanupHour { get; set; } = 4;

    public int CacheCleanupMinute { get; set; } = 0;
    /// <summary>
    /// 设备接入时是否自动续采（Paused / Interrupted / Failed）任务。
    /// false（默认）：不自动续采，卡片显示"已中断/已暂停"等用户点"重试"；
    /// true：自动 ResumeAsync 续采。
    /// </summary>
    public bool AutoResumeInterruptedOnReconnect { get; set; } = false;

    /// <summary>记录仪接入后是否自动开始采集（需求：默认自动）。</summary>
    public bool AutoCollectOnConnect { get; set; } = true;

    /// <summary>已采集过的文件自动跳过（按指纹）。</summary>
    public bool SkipCollected { get; set; } = true;

    /// <summary>紧急优先任务上限（操作员在工作台卡片上标记"优先"的数量限制）。</summary>
    public int MaxEmergencyTasks { get; set; } = 3;

    /// <summary>任务完成后自动擦除记录仪上已采集文件（需求：策略默认关闭）。</summary>
    public bool EraseAfterComplete { get; set; } = true;

    /// <summary>
    /// 同一任务内并发复制的文件数。
    /// 1 = 串行（MTP 推荐，默认）；UMS 可设为 2–4。
    /// 过大会导致 MTP 设备响应变慢甚至失败。
    /// </summary>
    public int ConcurrentCopyCount { get; set; } = 1;

    /// <summary>
    /// 字节块
    /// </summary>
    public int ChunkBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// 扫描文件扩展名白名单（含点，如 ".mp4" / ".jpg"）。空集合 = 不过滤。
    /// </summary>
    public List<string> ScanExtensions { get; set; } =
    [
        ".mp4", ".avi", ".flv", ".mov",
        ".wav", ".mp3", ".aac",
        ".jpg", ".bmp", ".jpeg", ".png",
        ".log"
    ];
   
    /// <summary>
    /// 只扫描这些相对路径前缀下的文件（如 "DCIM" / "RECORD/2024"）。空集合 = 不限。
    /// </summary>
    public List<string> ScanIncludePaths { get; set; } = [];

    /// <summary>
    /// 排除这些相对路径前缀下的文件（优先级高于 Include）。空集合 = 不排除。
    /// </summary>
    public List<string> ScanExcludePaths { get; set; } = [];


    /// <summary>模拟源每分块耗时（毫秒），用于演示可见进度与可靠的暂停/中断测试。</summary>
    public int SimulatedChunkDelayMs { get; set; } = 15;

    /// <summary>模拟记录仪生成的文件数（开发验证用）。</summary>
    public int SimulatedFileCount { get; set; } = 5;
}
