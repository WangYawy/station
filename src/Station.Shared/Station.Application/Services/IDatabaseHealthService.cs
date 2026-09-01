namespace Station.Application.Services;
/// <summary>
/// 数据库健康检查
/// </summary>
public interface IDatabaseHealthService
{
    /// <summary>检查数据库连接是否正常。</summary>
    bool IsConnected();
}
