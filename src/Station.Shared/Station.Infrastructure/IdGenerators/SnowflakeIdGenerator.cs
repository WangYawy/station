using SqlSugar;
using Station.Application.IdGenerators;

namespace Station.Infrastructure.IdGenerators;

public sealed class SnowflakeIdGenerator : IIdGenerator
{
    public long NextId() => SnowFlakeSingle.Instance.NextId();
}
