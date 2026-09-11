using SqlSugar;

namespace Station.Domain.Entities;

/// <summary>时钟状态（单行）：记录最近一次授权检查时间，用于时钟回拨检测。</summary>
[SugarTable("station_clock_state")]
public sealed class ClockState
{
    [SugarColumn(IsPrimaryKey = true)]
    public long Id { get; set; }

    public DateTime LastCheckAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
