namespace Station.Domain.Enums;

/// <summary>
/// 采集任务状态。每次记录仪连接产生一个采集任务；
/// 中途断线/拔出 → Interrupted（未完成文件标异常/取消）。
/// </summary>
public enum CollectTaskStatus
{
    Created = 0,
    Scanning = 1,
    Collecting = 2,
    Paused = 3,
    Completed = 4,
    Interrupted = 5,
    Failed = 6,
    Canceled = 7
}
