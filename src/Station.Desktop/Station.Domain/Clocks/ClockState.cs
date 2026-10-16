namespace Station.Domain.Clocks;

/// <summary>时钟状态（单行表，POCO）。</summary>
public sealed class ClockState
{
    public long Id { get; set; }
    public DateTime LastCheckAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
