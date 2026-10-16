namespace Station.Domain.Clocks;

public interface IClockStateRepository
{
    Task<ClockState?> GetAsync(CancellationToken ct = default);
    Task UpsertAsync(ClockState state, CancellationToken ct = default);
}
