namespace Station.Infrastructure.Db;

/// <summary>
/// 雪花ID配置，对应配置节 <c>Station:SnowFlake</c>。
/// </summary>
public sealed class SnowFlakeOptions
{
    public const string SectionName = "Station:SnowFlake";

    public int DatacenterId { get; set; } = 1;

    public int WorkId { get; set; } = 1;
}
