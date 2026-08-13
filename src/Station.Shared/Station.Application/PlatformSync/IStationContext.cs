namespace Station.Application.PlatformSync;

/// <summary>站点上下文：注册成功后回填平台分配的 StationId，供元数据上报使用。</summary>
public interface IStationContext
{
    long? StationId { get; }

    void Set(long stationId);
}

public sealed class StationContext : IStationContext
{
    public long? StationId { get; private set; }

    public void Set(long stationId) => StationId = stationId;
}
