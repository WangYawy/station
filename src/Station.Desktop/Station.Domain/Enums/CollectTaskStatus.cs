namespace Station.Domain.Enums;

/// <summary>
/// 采集任务状态。每次记录仪连接产生一个采集任务；
/// 中途断线/拔出 → Interrupted（未完成文件标异常/取消）。
/// </summary>
public enum CollectTaskStatus
{
    /// <summary>
    /// 已生成采集任务
    /// </summary>
    Created = 0,
    /// <summary>
    /// 扫描文件中
    /// </summary>
    Scanning = 1,
    /// <summary>
    /// 采集文件中
    /// </summary>
    Collecting = 2,
    /// <summary>
    /// 已暂停
    /// </summary>
    Paused = 3,
    /// <summary>
    /// 采集完成
    /// </summary>
    Completed = 4,
    /// <summary>
    /// 中断，断线/拔出
    /// </summary>
    Interrupted = 5,
    /// <summary>
    /// 采集失败
    /// </summary>
    Failed = 6,
    /// <summary>
    /// 已取消
    /// </summary>
    Canceled = 7
}
