
namespace Station.Application.Collecting;

/// <summary>采集任务编号生成器</summary>
public interface ICollectTaskNoGenerator
{
    /// <summary>
    /// 生成一个新的采集任务编号。
    /// 内部使用时间戳 + 同秒同设备序号，配合唯一索引兜底重试。
    /// </summary>
    /// <param name="stationCode">采集站编号（来自 ICollectorStationInfoService）</param>
    /// <param name="deviceId">设备标识（记录仪业务编号）</param>
    /// <param name="timestamp">任务创建时间（本地时间，用于编号）</param>
    Task<string> NextAsync(string stationCode, string deviceId, DateTime timestamp,
        CancellationToken ct = default);
}

/// <summary>采集文件编号生成器（任务上下文内使用）</summary>
public interface ICollectFileNoGenerator
{
    /// <summary>
    /// 创建一个任务级的文件编号生成上下文。
    /// 上下文内部维护文件序号，从当前任务已存在最大序号 + 1 开始。
    /// </summary>
    Task<IFileNoContext> CreateContextAsync(string taskNo, CancellationToken ct = default);
}

/// <summary>任务上下文内的文件编号生成器（非线程安全对象，每任务一个）</summary>
public interface IFileNoContext : IDisposable
{
    /// <summary>生成下一个文件编号</summary>
    string Next();

    /// <summary>当前已生成的序号</summary>
    int CurrentSeq { get; }
}
