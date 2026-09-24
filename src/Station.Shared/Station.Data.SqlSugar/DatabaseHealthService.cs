using Microsoft.Extensions.Options;
using Station.Data.Abstractions;
using Station.Data.SqlSugar;

namespace Station.Data;

/// <inheritdoc />
public sealed class DatabaseHealthService : IDatabaseHealthService
{
    private readonly ISqlSugarFactory _factory;
    private readonly DbOptions _options;

    public DatabaseHealthService(ISqlSugarFactory factory, IOptions<DbOptions> options)
    {
        _factory = factory;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<bool> IsConnected()
    {
        // 独立客户端 + using 保证资源及时释放，不影响共享客户端
        using var client = _factory.CreateClient(_options);
        var str = await client.Ado.GetStringAsync("select 1");
        return str == "1";
    }
}
