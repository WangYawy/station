namespace Station.Domain.Stations;

public interface IStationInfoRepository
{
    Task<StationInfo?> GetAsync(CancellationToken ct = default);
    Task UpsertAsync(StationInfo info, CancellationToken ct = default);
}
