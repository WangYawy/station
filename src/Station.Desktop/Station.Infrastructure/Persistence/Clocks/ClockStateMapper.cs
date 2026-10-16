using Station.Domain.Clocks;

namespace Station.Infrastructure.Persistence.Clocks;

internal static class ClockStateMapper
{
    public static ClockState ToDomain(ClockStatePo po)
        => new() { Id = po.Id, LastCheckAt = po.LastCheckAt, UpdatedAt = po.UpdatedAt };

    public static ClockStatePo ToPo(ClockState s)
        => new() { Id = s.Id, LastCheckAt = s.LastCheckAt, UpdatedAt = s.UpdatedAt };
}
