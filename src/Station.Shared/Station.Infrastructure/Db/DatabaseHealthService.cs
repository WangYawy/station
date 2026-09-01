using Station.Application.Services;

namespace Station.Infrastructure.Db;

public sealed class DatabaseHealthService : IDatabaseHealthService
{
    private readonly ISqlSugarFactory _factory;
    private readonly DbOptions _options;

    public DatabaseHealthService(ISqlSugarFactory factory, DbOptions options)
    {
        _factory = factory;
        _options = options;
    }

    public bool IsConnected()
    {
        // 此处保留了 using var client，但被隔离在 Infrastructure 内部
        using var client = _factory.CreateClient(_options);
        var str = client.Ado.GetString("select 1");
        return str == "1";
    }
}
