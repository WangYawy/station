namespace Station.Data.IdGeneration;

/// <summary>
/// 雪花算法配置，对应配置节 <c>Station:Data:Snowflake</c>。
/// </summary>
public sealed class SnowFlakeOptions
{
    /// <summary>默认配置节名。</summary>
    public const string SectionName = "Data:Snowflake";

    /// <summary>数据中心 ID（0-31）。</summary>
    public long DatacenterId { get; set; }

    /// <summary>工作机器 ID（0-31）。</summary>
    public long WorkId { get; set; }
}
