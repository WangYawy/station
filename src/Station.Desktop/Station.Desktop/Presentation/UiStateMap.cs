using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Desktop.Presentation;

/// <summary>任务状态 → 展示投影。</summary>
public sealed record CollectTaskStatusPresentation(
    string Text, string BadgeBrush, string Foreground,
    bool CanPause, bool CanResume, bool CanCancel, bool CanRetry, bool CanPriority);

/// <summary>链路状态 → 展示投影。</summary>
public sealed record DeviceLinkStatePresentation(
    string Brush, string Text, bool CanRetryIdentify);

/// <summary>状态到 UI 的集中映射表。新增状态只需改这里。</summary>
public static class UiStateMap
{
    public static CollectTaskStatusPresentation For(CollectTaskStatus s) => s switch
    {
        CollectTaskStatus.Scanning => new("扫描中", "#e0e7ff", "#4338ca", false, false, true, false, false),
        CollectTaskStatus.Collecting => new("采集中", "#dbeafe", "#1d4ed8", true, false, true, false, true),
        CollectTaskStatus.Paused => new("已暂停", "#fef3c7", "#b45309", false, true, true, false, true),
        CollectTaskStatus.Failed => new("故障", "#fee2e2", "#b91c1c", false, false, false, true, false),
        CollectTaskStatus.Completed => new("已完成", "#dcfce7", "#15803d", false, false, false, false, false),
        CollectTaskStatus.Canceled => new("已取消", "#e2e8f0", "#475569", false, false, false, false, false),
        CollectTaskStatus.Interrupted => new("已中断", "#fef3c7", "#92400e", false, false, false, false, false),
        CollectTaskStatus.Created => new("准备中", "#f1f5f9", "#64748b", false, false, false, false, false),
        CollectTaskStatus.Waiting => new("待开始", "#f1f5f9", "#64748b", false, false, false, false, false),
        _ => new("--", "#f1f5f9", "#94a3b8", false, false, false, false, false)
    };

    public static DeviceLinkStatePresentation For(DeviceLinkState s) => s switch
    {
        DeviceLinkState.Verifying => new("#f59e0b", "识别中", false),
        DeviceLinkState.Bound => new("#22c55e", "已连接", false),
        DeviceLinkState.Rejected => new("#ef4444", "拒绝连接", true),
        _ => new("#cbd5e1", "空闲", false)
    };
}
