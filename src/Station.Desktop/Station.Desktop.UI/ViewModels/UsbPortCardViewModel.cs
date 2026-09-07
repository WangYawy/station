using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Collecting;
using Station.Application.UsbPortCard;
using Station.Contracts;
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

    /// <summary>设备唯一标识（用于匹配设备事件）</summary>
    [ObservableProperty]
    private string? _deviceKey;

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

    // 辅助计算属性（用于统计）
    public bool IsCollecting => !IsIdle &&
        (StatusText == "采集中" || StatusText == "扫描中");

    public bool IsPaused => !IsIdle && StatusText == "已暂停";


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

    #region 公共更新方法
    /// <summary>
    /// 从完整快照 DTO 更新卡片（用于启动加载和兜底刷新）
    /// </summary>
    public void UpdateFromDto(UsbPortCardDto dto)
    {
        DeviceKey = dto.DeviceKey;

        if (!dto.IsConnected)
        {
            SetIdle();
            DeviceText = "-- 等待设备连接";
            return;
        }

        // 设备在线但无任务 -> 显示为“空闲”状态
        if (dto.TaskId is null)
        {
            SetIdle();
            DeviceText = dto.DeviceName ?? "设备已连接";
            StatusText = "空闲";
            StatusBadgeBrush = "#f1f5f9";
            StatusForeground = "#94a3b8";
            IsIdle = true;
            Opacity = 0.55;
            return;
        }

        // 有任务：复用现有的 UpdateFromTask 逻辑（但数据来源不同）
        // 直接调用内部填充方法
        ApplyTaskData(
            taskId: dto.TaskId.Value,
            taskNo: dto.TaskNo ?? string.Empty,
            recorderName: dto.DeviceName ?? "未知设备",
            protocol: dto.Protocol ?? ProtocolType.Ums,
            isAuto: true, // 快照中无此信息，可默认或由服务传入
            isEmergency: dto.IsEmergency,
            status: dto.Status ?? CollectTaskStatus.Created,
            totalFiles: dto.TotalFiles,
            collectedFiles: dto.CollectedFiles,
            totalBytes: dto.TotalBytes,
            collectedBytes: dto.CollectedBytes,
            speed: dto.SpeedBytesPerSecond
        );
    }
    /// <summary>
    /// 增量更新：仅更新进度和速度（由事件触发）
    /// </summary>
    public void UpdateProgress(double progressPercent, double speedBytesPerSecond)
    {
        Progress = Math.Clamp(progressPercent, 0, 100);
        ProgressText = $"{Progress:F0}%";
        SpeedText = speedBytesPerSecond > 0
            ? $"{speedBytesPerSecond / 1024 / 1024:F1} MB/s"
            : "-- MB/s";
    }

    /// <summary>
    /// 增量更新：状态或紧急标记变更（由事件触发）
    /// </summary>
    public void UpdateStatus(CollectTaskStatus status, bool isEmergency)
    {
        IsEmergency = isEmergency;
        ApplyStatusStyle(status);
        NotifyCommands();
    }

    /// <summary>
    /// 设备接入：将空闲卡片激活为“设备在线，等待任务”
    /// </summary>
    public void SetDeviceOnline(string key, string name, ProtocolType protocol)
    {
        DeviceKey = key;
        DeviceText = name;
        MetaText = $"{protocol} · 等待采集";
        StatusText = "空闲";
        StatusBadgeBrush = "#f1f5f9";
        StatusForeground = "#94a3b8";
        IsIdle = false;
        Opacity = 1;
        // 清除旧任务数据
        TaskId = null;
        TaskNo = string.Empty;
        StorageText = "--";
        Progress = 0;
        ProgressText = "0%";
        SpeedText = "-- MB/s";
        CanPause = CanResume = CanCancel = CanRetry = CanPriority = false;
        IsEmergency = false;
        AccentBrush = "#2563eb";
        CardBackground = "White";
        PriorityButtonText = "优先";
        PriorityButtonBrush = "#ea580c";
        NotifyCommands();
    }

    /// <summary>
    /// 重置为空闲状态（设备拔出或任务结束）
    /// </summary>
    public void SetIdle()
    {
        DeviceKey = null;
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
    /// 设置卡片尺寸（布局重建时调用）
    /// </summary>
    public void SetCardSize(double width, double height)
    {
        CardWidth = width;
        CardHeight = height;
    }

    /// <summary>
    /// 从任务 DTO 更新（保留给命令操作后的刷新，或兼容旧代码）
    /// </summary>
    public void UpdateFromTask(CollectTaskDto task)
    {
        ApplyTaskData(
            taskId: task.TaskId,
            taskNo: task.TaskNo,
            recorderName: task.RecorderName,
            protocol: task.Protocol,
            isAuto: task.IsAuto,
            isEmergency: task.IsEmergency,
            status: task.Status,
            totalFiles: task.TotalFiles,
            collectedFiles: task.CollectedFiles,
            totalBytes: task.TotalBytes,
            collectedBytes: task.CollectedBytes,
            speed: task.SpeedBytesPerSecond
        );
    }
    #endregion

    #region 内部辅助方法

    private void ApplyTaskData(
        long taskId,
        string taskNo,
        string recorderName,
        ProtocolType protocol,
        bool isAuto,
        bool isEmergency,
        CollectTaskStatus status,
        int totalFiles,
        int collectedFiles,
        long totalBytes,
        long collectedBytes,
        double speed)
    {
        TaskId = taskId;
        TaskNo = taskNo;
        IsIdle = false;
        Opacity = 1;
        IsEmergency = isEmergency;
        AccentBrush = isEmergency ? "#ef4444" : "#2563eb";
        CardBackground = isEmergency ? "#fef2f2" : "White";
        PriorityButtonText = isEmergency ? "取消优先" : "优先";
        PriorityButtonBrush = isEmergency ? "#ef4444" : "#ea580c";
        DeviceText = recorderName;
        MetaText = $"{ProtocolText(protocol)} · {(isAuto ? "自动采集" : "手动采集")}";
        StorageText = $"文件 {collectedFiles}/{totalFiles} · 已采集 {FormatSize(collectedBytes)}";

        var percent = totalFiles > 0
            ? (double)collectedFiles / totalFiles
            : totalBytes > 0
                ? (double)collectedBytes / totalBytes
                : 0d;
        Progress = Math.Clamp(percent * 100, 0, 100);
        ProgressText = $"{Progress:F0}%";
        SpeedText = speed > 0
            ? $"{speed / 1024 / 1024:F1} MB/s"
            : "-- MB/s";

        ApplyStatusStyle(status);
        NotifyCommands();
    }
    private void ApplyStatusStyle(CollectTaskStatus status)
    {
        // 重置所有按钮权限
        CanPause = CanResume = CanCancel = CanRetry = CanPriority = false;

        switch (status)
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
    private static string ProtocolText(ProtocolType protocol) => protocol switch
    {
        ProtocolType.Mtp => "MTP",
        ProtocolType.PrivateSdk => "私有SDK",
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
    #endregion
}
