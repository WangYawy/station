namespace Station.Data.Abstractions;

/// <summary>数据库健康检查（只读探测）。</summary>
public interface IDatabaseHealthService
{
    /// <summary>探测数据库是否可用。</summary>
    Task<bool> IsConnected();
}
