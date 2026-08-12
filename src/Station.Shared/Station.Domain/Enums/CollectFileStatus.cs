namespace Station.Domain.Enums;

/// <summary>
/// 采集文件状态。采集=从记录仪复制到本地缓存并校验。
/// 中断时：进行中文件→Abnormal，未开始文件→Canceled。
/// </summary>
public enum CollectFileStatus
{
    Pending = 0,
    Copying = 1,
    Verifying = 2,
    Completed = 3,
    Skipped = 4,
    Failed = 5,
    Abnormal = 6,
    Canceled = 7
}
