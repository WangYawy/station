using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Collecting;
using Station.Domain.Enums;

namespace Station.Desktop.UI.ViewModels;

/// <summary>
/// 工作台 30 路 USB 采集通道卡片：实时反映该端口当前任务（采集中/已暂停/空闲），
/// 提供暂停/恢复/取消/重试/查看明细操作。
/// </summary>
public partial class UsbPortCardViewModel : ObservableObject
{
    #region 属性
    /// <summary>
    /// 端口号
    /// </summary>
    public string PortText { get; }

    [ObservableProperty]
    private long? _taskId;

    [ObservableProperty]
    private string _taskNo = string.Empty;

    /// <summary>
    /// 状态文本
    /// </summary>
    [ObservableProperty]
    private string _statusText = "空闲";

    /// <summary>
    /// 状态标识画笔
    /// </summary>
    [ObservableProperty]
    private string _statusBadgeBrush = "#f1f5f9";
    /// <summary>
    /// 状态标识
    /// </summary>
    [ObservableProperty]
    private string _statusForeground = "#94a3b8";
    /// <summary>
    /// 设备状态
    /// </summary>
    [ObservableProperty]
    private string _deviceText = "-- 等待设备连接";
    
    [ObservableProperty]
    private string _metaText = "--";
    /// <summary>
    /// 存储空间文件
    /// </summary>
    [ObservableProperty]
    private string _storageText = "--";
    /// <summary>
    /// 采集进度
    /// </summary>
    [ObservableProperty]
    private double _progress;
    /// <summary>
    /// 采集进度文本
    /// </summary>
    [ObservableProperty]
    private string _progressText = "0%";
    /// <summary>
    /// 采集速度文本
    /// </summary>
    [ObservableProperty]
    private string _speedText = "-- MB/s";
    /// <summary>
    /// 是否空闲状态
    /// </summary>
    [ObservableProperty]
    private bool _isIdle = true;
    /// <summary>
    /// 透明度
    /// </summary>
    [ObservableProperty]
    private double _opacity = 0.55;
    /// <summary>
    /// 卡片宽
    /// </summary>
    [ObservableProperty]
    private double _cardWidth;
    /// <summary>
    /// 卡片高
    /// </summary>
    [ObservableProperty]
    private double _cardHeight;
    /// <summary>
    /// 是否紧急优先采集
    /// </summary>
    [ObservableProperty]
    private bool _isEmergency;
    /// <summary>
    /// 重点笔刷
    /// </summary>
    [ObservableProperty]
    private string _accentBrush = "#2563eb";
    /// <summary>
    /// 卡片背景色
    /// </summary>
    [ObservableProperty]
    private string _cardBackground = "White";
    /// <summary>
    /// 是否可以优先
    /// </summary>
    [ObservableProperty]
    private bool _canPriority;
    /// <summary>
    /// 优先按钮文本
    /// </summary>
    [ObservableProperty]
    private string _priorityButtonText = "优先";

    [ObservableProperty]
    private string _priorityButtonBrush = "#ea580c";
    /// <summary>
    /// 是否可以暂停
    /// </summary>
    [ObservableProperty]
    private bool _canPause;
    /// <summary>
    /// 是否可以恢复
    /// </summary>
    [ObservableProperty]
    private bool _canResume;
    /// <summary>
    /// 是否可以取消
    /// </summary>
    [ObservableProperty]
    private bool _canCancel;
    /// <summary>
    /// 是否可以重试
    /// </summary>
    [ObservableProperty]
    private bool _canRetry;
    /// <summary>
    /// 暂停命令
    /// </summary>
    public IRelayCommand PauseCommand { get; }
    /// <summary>
    /// 回复命令
    /// </summary>
    public IRelayCommand ResumeCommand { get; }
    /// <summary>
    /// 取消命令
    /// </summary>
    public IRelayCommand CancelCommand { get; }
    /// <summary>
    /// 重试命令
    /// </summary>
    public IRelayCommand RetryCommand { get; }
    /// <summary>
    /// 紧急优先命令
    /// </summary>
    public IRelayCommand PriorityCommand { get; }
    #endregion 属性
    public UsbPortCardViewModel(
        int portIndex,
        Action<UsbPortCardViewModel> pause,
        Action<UsbPortCardViewModel> resume,
        Action<UsbPortCardViewModel> cancel,
        Action<UsbPortCardViewModel> retry,
        Action<UsbPortCardViewModel> priority,
        double cardWidth,
        double cardHeight)
    {
        PortText = $"#{portIndex:D2}";
        CardWidth = cardWidth;
        CardHeight = cardHeight;
        PauseCommand = new RelayCommand(() => pause(this), () => CanPause);
        ResumeCommand = new RelayCommand(() => resume(this), () => CanResume);
        CancelCommand = new RelayCommand(() => cancel(this), () => CanCancel);
        RetryCommand = new RelayCommand(() => retry(this), () => CanRetry);
        PriorityCommand = new RelayCommand(() => priority(this), () => CanPriority);
    }

    #region 方法
    /// <summary>
    /// 设置为空闲
    /// </summary>
    public void SetIdle()
    {
        TaskId = null;
        TaskNo = string.Empty;
        StatusText = "空闲";
        StatusBadgeBrush = "#f1f5f9";
        StatusForeground = "#94a3b8";
        DeviceText = "-- 等待设备连接";
        MetaText = "--";
        StorageText = "--";
        Progress = 0;
        ProgressText = "0%";
        SpeedText = "-- MB/s";
        IsIdle = true;
        Opacity = 0.55;
        IsEmergency = false;
        AccentBrush = "#2563eb";
        CardBackground = "White";
        PriorityButtonText = "优先";
        PriorityButtonBrush = "#ea580c";
        CanPause = CanResume = CanCancel = CanRetry = CanPriority = false;
        NotifyCommands();
    }
    /// <summary>
    /// 设置卡片大小
    /// </summary>
    public void SetCardSize(double width, double height)
    {
        CardWidth = width;
        CardHeight = height;
    }
    /// <summary>
    /// 更新表单任务信息
    /// </summary>
    /// <param name="task">采集任务</param>
    public void UpdateFromTask(CollectTaskDto task)
    {
        TaskId = task.TaskId;
        TaskNo = task.TaskNo;
        IsIdle = false;
        Opacity = 1;
        IsEmergency = task.IsEmergency;
        AccentBrush = task.IsEmergency ? "#ef4444" : "#2563eb";
        CardBackground = task.IsEmergency ? "#fef2f2" : "White";
        PriorityButtonText = task.IsEmergency ? "取消优先" : "优先";
        PriorityButtonBrush = task.IsEmergency ? "#ef4444" : "#ea580c";
        DeviceText = task.RecorderName;
        MetaText = $"{ProtocolText(task)} · {(task.IsAuto ? "自动采集" : "手动采集")}";
        StorageText = $"文件 {task.CollectedFiles}/{task.TotalFiles} · 已采集 {FormatSize(task.CollectedBytes)}";

        var percent = task.TotalFiles > 0
            ? (double)task.CollectedFiles / task.TotalFiles
            : task.TotalBytes > 0
                ? (double)task.CollectedBytes / task.TotalBytes
                : 0d;
        Progress = Math.Clamp(percent * 100, 0, 100);
        ProgressText = $"{Progress:F0}%";
        SpeedText = task.SpeedBytesPerSecond > 0
            ? $"{task.SpeedBytesPerSecond / 1024 / 1024:F1} MB/s"
            : "-- MB/s";

        switch (task.Status)
        {
            case CollectTaskStatus.Scanning:
            case CollectTaskStatus.Collecting:
                StatusText = "采集中";
                StatusBadgeBrush = "#dbeafe";
                StatusForeground = "#1d4ed8";
                CanPause = true;
                CanCancel = true;
                CanPriority = true;
                break;
            case CollectTaskStatus.Paused:
                StatusText = "已暂停";
                StatusBadgeBrush = "#fef3c7";
                StatusForeground = "#b45309";
                CanResume = true;
                CanCancel = true;
                CanPriority = true;
                break;
            case CollectTaskStatus.Failed:
                StatusText = "故障";
                StatusBadgeBrush = "#fee2e2";
                StatusForeground = "#b91c1c";
                CanRetry = true;
                CanPriority = false;
                break;
            case CollectTaskStatus.Completed:
                StatusText = "已完成";
                StatusBadgeBrush = "#dcfce7";
                StatusForeground = "#15803d";
                CanPriority = false;
                break;
            default:
                StatusText = "待开始";
                StatusBadgeBrush = "#f1f5f9";
                StatusForeground = "#64748b";
                CanPriority = false;
                break;
        }

        NotifyCommands();
    }

    /// <summary>
    /// 通知命令
    /// </summary>
    private void NotifyCommands()
    {
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        PriorityCommand.NotifyCanExecuteChanged();
    }
    /// <summary>
    /// 设备连接方式文本
    /// </summary>
    /// <param name="task">采集任务</param>
    /// <returns></returns>
    private static string ProtocolText(CollectTaskDto task) => task.Protocol switch
    {
        Station.Contracts.ProtocolType.Mtp => "MTP",
        Station.Contracts.ProtocolType.PrivateSdk => "私有SDK",
        _ => "UMS"
    };
    /// <summary>
    /// 格式化大小
    /// </summary>
    /// <param name="bytes">字节</param>
    /// <returns></returns>
    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:F1} {units[unit]}";
    }
    #endregion 方法
}
