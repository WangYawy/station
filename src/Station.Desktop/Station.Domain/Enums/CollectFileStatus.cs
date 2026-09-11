namespace Station.Domain.Enums;

/// <summary>
/// 采集文件状态。采集=从记录仪复制到本地缓存并校验。
/// 中断时：进行中文件→Abnormal，未开始文件→Canceled。
/// </summary>
public enum CollectFileStatus
{
    /// <summary>
    /// 待处理
    /// </summary>
    Pending = 0,
    /// <summary>
    /// 复制中
    /// </summary>
    Copying = 1,
    /// <summary>
    /// 验证中
    /// </summary>
    Verifying = 2,
    /// <summary>
    /// 已完成
    /// </summary>
    Completed = 3,
    /// <summary>
    /// 已跳过
    /// </summary>
    Skipped = 4,
    /// <summary>
    /// 失败
    /// </summary>
    Failed = 5,
    /// <summary>
    /// 异常终止
    /// </summary>
    Abnormal = 6,
    /// <summary>
    /// 取消
    /// </summary>
    Canceled = 7
}
