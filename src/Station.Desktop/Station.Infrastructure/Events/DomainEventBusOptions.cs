namespace Station.Infrastructure.Events;

public sealed class DomainEventBusOptions
{
    public const string SectionName = "Station:Events";

    public DomainEventDispatchMode Mode { get; set; } = DomainEventDispatchMode.Channel;

    /// <summary>Channel 有界容量。溢出时按策略处理。</summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Channel 模式批量派发大小。</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>Channel 模式最大等待刷盘时间（毫秒）。</summary>
    public int FlushIntervalMs { get; set; } = 100;

    /// <summary>内存事件保留时长（天）。用于阶段一的事件重放。</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>内存保留最多事件条数（环形缓冲上限）。</summary>
    public int RetentionMaxCount { get; set; } = 100_000;

    /// <summary>Channel 溢出策略。</summary>
    public ChannelOverflowPolicy OverflowPolicy { get; set; } = ChannelOverflowPolicy.DropOldest;

    /// <summary>进度合并器刷盘间隔（毫秒）。默认 200ms。</summary>
    public int ProgressFlushIntervalMs { get; set; } = 200;
}

public enum DomainEventDispatchMode
{
    /// <summary>立即同步调用所有 handler（开发调试用）。</summary>
    Sync,

    /// <summary>写入内存 Channel，后台批量派发（阶段一生产推荐）。</summary>
    Channel,

    /// <summary>写入 outbox 表，后台持久化派发（阶段二）。</summary>
    Outbox
}

public enum ChannelOverflowPolicy
{
    DropOldest,
    DropNewest,
    BlockProducer
}
