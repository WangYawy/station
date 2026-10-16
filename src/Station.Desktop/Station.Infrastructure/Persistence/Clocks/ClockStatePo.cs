using SqlSugar;

namespace Station.Infrastructure.Persistence.Clocks;

[SugarTable("station_clock_state")]
public sealed class ClockStatePo
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    public DateTime LastCheckAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
