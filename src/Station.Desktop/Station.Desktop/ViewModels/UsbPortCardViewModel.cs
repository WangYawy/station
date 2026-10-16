using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Collecting.Dtos;
using Station.Application.UsbPortCards;
using Station.Contracts;
using Station.Domain.Collecting;

namespace Station.Desktop.ViewModels;

/// <summary>
/// 工作台 30 路 USB 采集通道卡片：实时反映该端口当前任务（采集中/已暂停/空闲），
/// 提供暂停/恢复/取消/重试/查看明细操作。
/// </summary>
public partial class UsbPortCardViewModel : ObservableObject
{
    // 命令状态缓存（仅变化时通知）
    private bool _lastCanPause, _lastCanResume, _lastCanCancel,
                 _lastCanRetry, _lastCanPriority, _lastHasTask;
    // 进度文本噪声过滤
    private double _lastSpeedMb = -1;

    #region 属性
    public string PortText { get; }

    [ObservableProperty]
    private string? _deviceKey;

    // =================== 设备连接维度 ===================

    [ObservableProperty]
    private DeviceLinkState _linkState = DeviceLinkState.Offline;

    [ObservableProperty]
    private string _linkBrush = "#cbd5e1";

    [ObservableProperty]
    private string _linkText = "空闲";

    [ObservableProperty]
    private string _deviceText = "-- 等待设备连接";

    [ObservableProperty] private string _deviceSpaceText = string.Empty;
    [ObservableProperty] private bool _hasDeviceSpace;

    // =================== 任务维度 ===================

    [ObservableProperty]
    private CollectTaskStatus? _taskStatus;

    [ObservableProperty]
    private long? _taskId;

    [ObservableProperty]
    private string _taskNo = string.Empty;

    [ObservableProperty]
    private string _statusText = "--";

    [ObservableProperty]
    private string _statusBadgeBrush = "#f1f5f9";

    [ObservableProperty]
    private string _statusForeground = "#94a3b8";

    [ObservableProperty]
    private string _storageText = "--";

    // =================== 操作人维度（p0 占位，未填充） ===================
    [ObservableProperty] private string _operatorText = string.Empty;
    [ObservableProperty] private bool _hasOperator;

    // ================== 提示维度 ==================
    [ObservableProperty] private string _hintText = string.Empty;
    [ObservableProperty] private string _hintBrush = "#b91c1c";
    [ObservableProperty] private bool _hasHint;

    // =================== 展示信息 ===================
    [ObservableProperty] private string _metaText = "--";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "0%";
    [ObservableProperty] private string _speedText = "-- MB/s";

    [ObservableProperty] private bool _isIdle = true;
    [ObservableProperty] private double _opacity = 0.55;
    [ObservableProperty] private bool _isEmergency;
    [ObservableProperty] private string _accentBrush = "#2563eb";
    [ObservableProperty] private string _cardBackground = "White";
    [ObservableProperty] private bool _canPriority;
    [ObservableProperty] private string _priorityButtonText = "优先";
    [ObservableProperty] private string _priorityButtonBrush = "#ea580c";
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _canResume;
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private bool _canRetry;

    // =================== 派生状态 ===================
    public bool IsScanning => TaskStatus == CollectTaskStatus.Scanning;
    public bool IsCollecting => TaskStatus == CollectTaskStatus.Collecting;
    public bool IsPaused => TaskStatus == CollectTaskStatus.Paused;
    public bool IsFailedState => TaskStatus == CollectTaskStatus.Failed;
    public bool IsCompletedState => TaskStatus == CollectTaskStatus.Completed;
    public bool IsPendingState => TaskStatus == CollectTaskStatus.Created;

    /// <summary>是否有任务（供 XAML / 命令 CanExecute 共用）</summary>
    public bool HasTask => TaskId is not null;

    // =================== 命令 ===================
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand ResumeCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RetryCommand { get; }
    public IRelayCommand PriorityCommand { get; }
    public IRelayCommand DetailsCommand { get; }
    #endregion

    public UsbPortCardViewModel(
        int portIndex,
        Action<UsbPortCardViewModel> pause,
        Action<UsbPortCardViewModel> resume,
        Action<UsbPortCardViewModel> cancel,
        Action<UsbPortCardViewModel> retry,
        Action<UsbPortCardViewModel> priority,
        Action<UsbPortCardViewModel> details)
    {
        PortText = $"#{portIndex:D2}";

        PauseCommand = new RelayCommand(() => pause(this), () => CanPause);
        ResumeCommand = new RelayCommand(() => resume(this), () => CanResume);
        CancelCommand = new RelayCommand(() => cancel(this), () => CanCancel);
        RetryCommand = new RelayCommand(() => retry(this), () => CanRetry);
        PriorityCommand = new RelayCommand(() => priority(this), () => CanPriority);
        DetailsCommand = new RelayCommand(() => details(this), () => HasTask);
    }

    #region 公共更新方法

    /// <summary>从完整快照 DTO 更新卡片（快照权威，含链路态；仅进度字段取 max 防回退）</summary>
    public void UpdateFromDto(UsbPortCardDto dto)
    {
        // ============ 设备维度 ============
        if (!dto.IsConnected)
        {
            if (DeviceKey is not null || LinkState != DeviceLinkState.Offline)
            {
                ApplyLinkState(DeviceLinkState.Offline);
                DeviceKey = null;
            }
            if (TaskId is null)
            {
                DeviceText = "-- 等待设备连接";
                MetaText = "--";
                Opacity = 0.55;
            }
            SetDeviceSpace(null, null);
        }
        else
        {
            DeviceKey = dto.DeviceKey;
            if (LinkState != dto.LinkState)
                ApplyLinkState(dto.LinkState);

            // 无任务时才由设备维度决定显示名/协议
            if (TaskId is null)
            {
                DeviceText = dto.DeviceName ?? "设备已连接";
                MetaText = dto.Protocol is { } p
                    ? $"{ProtocolText(p)} · 等待采集"
                    : "--";
            }
            Opacity = 1;
            SetDeviceSpace(dto.DeviceAvailableBytes, dto.DeviceTotalBytes);
        }

        // ============ 任务维度 ============
        if (dto.TaskId is null)
        {
            if (TaskId is not null) ApplyTaskIdle();
            return;
        }

        // 判断是否需要写进度：任务 ID 变了无条件写；同任务时 dto 进度落后则跳过
        var sameTask = TaskId == dto.TaskId;
        var dtoPercent = dto.TotalFiles > 0
            ? (double)dto.CollectedFiles / dto.TotalFiles
            : dto.TotalBytes > 0
                ? (double)dto.CollectedBytes / dto.TotalBytes
                : 0d;
        var dtoProgress = Math.Clamp(dtoPercent * 100, 0, 100);
        var writeProgress = !sameTask || dtoProgress >= Progress;

        ApplyTaskData(
            dto.TaskId.Value, dto.TaskNo ?? "", dto.DeviceName ?? "未知设备",
            dto.Protocol ?? ProtocolType.Ums, true, dto.IsEmergency,
            dto.Status ?? CollectTaskStatus.Created,
            dto.TotalFiles, dto.CollectedFiles,
            dto.TotalBytes, dto.CollectedBytes,
            dto.SpeedBytesPerSecond,
            writeProgress: writeProgress);
    }

    /// <summary>增量更新：进度和速度（高频入口，含去噪）</summary>
    public void UpdateProgress(double progressPercent, double speedBytesPerSecond)
    {
        var p = Math.Clamp(progressPercent, 0, 100);
        if (Math.Abs(Progress - p) >= 0.05)
        {
            Progress = Math.Round(p, 1);
            var newText = $"{Progress:F0}%";
            if (!string.Equals(ProgressText, newText, StringComparison.Ordinal))
                ProgressText = newText;
        }

        var speedMb = speedBytesPerSecond > 0
            ? Math.Round(speedBytesPerSecond / 1048576d, 1)
            : 0d;
        if (Math.Abs(_lastSpeedMb - speedMb) >= 0.1)
        {
            _lastSpeedMb = speedMb;
            SpeedText = speedMb > 0 ? $"{speedMb:F1} MB/s" : "-- MB/s";
        }
    }

    /// <summary>增量更新：任务状态或紧急标记（由事件触发）</summary>
    public void UpdateStatus(CollectTaskStatus status, bool isEmergency)
    {
        var emergencyChanged = IsEmergency != isEmergency;
        IsEmergency = isEmergency;
        ApplyStatusStyle(status);
        if (emergencyChanged) UpdatePriorityPresentation();
        NotifyCommands();
    }

    /// <summary>设备接入：进入"识别中"链路态</summary>
    public void SetDeviceOnline(string key, string name, ProtocolType protocol)
    {
        DeviceKey = key;
        DeviceText = name;
        MetaText = $"{protocol} · 等待采集";
        ApplyLinkState(DeviceLinkState.Verifying);

        if (TaskId is not null) ApplyTaskIdle();

        ClearHint();
        Opacity = 1;
    }

    /// <summary>设备拔出：链路态回 Offline；无任务时任务维度一并归零</summary>
    public void SetDeviceOffline()
    {
        ApplyLinkState(DeviceLinkState.Offline);
        DeviceKey = null;
        if (TaskId is null)
        {
            DeviceText = "-- 等待设备连接";
            MetaText = "--";
            ApplyTaskIdle();
            Opacity = 0.55;
        }
        SetDeviceSpace(null, null);
        NotifyCommands();
    }

    /// <summary>整体归零</summary>
    public void SetIdle()
    {
        DeviceKey = null;
        ApplyLinkState(DeviceLinkState.Offline);
        DeviceText = "-- 等待设备连接";
        MetaText = "--";
        Opacity = 0.55;
        ApplyTaskIdle();
        SetDeviceSpace(null, null);
        ClearHint();
    }

    public void SetBound()
    {
        ApplyLinkState(DeviceLinkState.Bound);
        ClearHint();
    }

    public void SetRejected(string reason)
    {
        ApplyLinkState(DeviceLinkState.Rejected);
        SetHint(reason, "#b91c1c");
    }

    public void SetWarning(string message) => SetHint(message, "#b45309");

    public void SetDeviceSpace(long? availableBytes, long? totalBytes)
    {
        if (availableBytes is null or < 0 || totalBytes is null or <= 0)
        {
            DeviceSpaceText = string.Empty;
            HasDeviceSpace = false;
            return;
        }
        DeviceSpaceText = $"可用 {FormatSize(availableBytes.Value)} / {FormatSize(totalBytes.Value)}";
        HasDeviceSpace = true;
    }

    public void UpdateFromTask(CollectTaskDto task)
    {
        ApplyTaskData(
            task.TaskId, task.TaskNo, task.RecorderName, task.Protocol,
            task.IsAuto, task.IsEmergency, task.Status,
            task.TotalFiles, task.CollectedFiles,
            task.TotalBytes, task.CollectedBytes,
            task.SpeedBytesPerSecond,
            writeProgress: true);
    }

    #endregion

    #region 内部辅助方法

    private void ApplyTaskData(
        long taskId, string taskNo, string recorderName, ProtocolType protocol,
        bool isAuto, bool isEmergency, CollectTaskStatus status,
        int totalFiles, int collectedFiles,
        long totalBytes, long collectedBytes, double speed,
        bool writeProgress)
    {
        var isNewTask = TaskId is null;

        TaskId = taskId;
        TaskNo = taskNo;
        IsIdle = false;
        Opacity = 1;
        IsEmergency = isEmergency;

        if (isNewTask)
            DeviceText = recorderName;   // 只在首建时覆盖设备展示名

        MetaText = $"{ProtocolText(protocol)} · {(isAuto ? "自动采集" : "手动采集")}";
        StorageText = $"扫描 {totalFiles} · 已上传 {collectedFiles}";

        if (writeProgress)
        {
            var percent = totalFiles > 0
                ? (double)collectedFiles / totalFiles
                : totalBytes > 0
                    ? (double)collectedBytes / totalBytes
                    : 0d;
            Progress = Math.Clamp(percent * 100, 0, 100);
            ProgressText = $"{Progress:F0}%";

            var speedMb = speed > 0 ? Math.Round(speed / 1048576d, 1) : 0d;
            _lastSpeedMb = speedMb;
            SpeedText = speedMb > 0 ? $"{speedMb:F1} MB/s" : "-- MB/s";
        }

        ApplyStatusStyle(status);
        UpdatePriorityPresentation();
        NotifyCommands();
    }

    private void ApplyTaskIdle()
    {
        TaskId = null;
        TaskNo = string.Empty;
        IsIdle = true;

        TaskStatus = null;
        StatusText = "--";
        StatusBadgeBrush = "#f1f5f9";
        StatusForeground = "#94a3b8";

        StorageText = "--";
        Progress = 0;
        ProgressText = "0%";
        SpeedText = "-- MB/s";
        _lastSpeedMb = -1;

        IsEmergency = false;
        AccentBrush = "#2563eb";
        CardBackground = "White";
        PriorityButtonText = "优先";
        PriorityButtonBrush = "#ea580c";

        CanPause = CanResume = CanCancel = CanRetry = CanPriority = false;
        NotifyCommands();
    }

    private void ApplyStatusStyle(CollectTaskStatus status)
    {
        if (TaskStatus == status) return;

        CanPause = CanResume = CanCancel = CanRetry = CanPriority = false;

        switch (status)
        {
            case CollectTaskStatus.Scanning:
                StatusText = "扫描中";
                StatusBadgeBrush = "#e0e7ff";
                StatusForeground = "#4338ca";
                CanCancel = true;
                break;

            case CollectTaskStatus.Collecting:
                StatusText = "采集中";
                StatusBadgeBrush = "#dbeafe";
                StatusForeground = "#1d4ed8";
                CanPause = CanCancel = CanPriority = true;
                break;

            case CollectTaskStatus.Paused:
                StatusText = "已暂停";
                StatusBadgeBrush = "#fef3c7";
                StatusForeground = "#b45309";
                CanResume = CanCancel = CanPriority = true;
                break;

            case CollectTaskStatus.Failed:
                StatusText = "故障";
                StatusBadgeBrush = "#fee2e2";
                StatusForeground = "#b91c1c";
                CanRetry = true;
                break;

            case CollectTaskStatus.Completed:
                StatusText = "已完成";
                StatusBadgeBrush = "#dcfce7";
                StatusForeground = "#15803d";
                break;

            case CollectTaskStatus.Canceled:
                StatusText = "已取消";
                StatusBadgeBrush = "#e2e8f0";
                StatusForeground = "#475569";
                break;

            case CollectTaskStatus.Interrupted:
                StatusText = "已中断";
                StatusBadgeBrush = "#fef3c7";
                StatusForeground = "#92400e";
                break;

            case CollectTaskStatus.Created:
                StatusText = "待开始";
                StatusBadgeBrush = "#f1f5f9";
                StatusForeground = "#64748b";
                break;

            default:
                StatusText = "--";
                StatusBadgeBrush = "#f1f5f9";
                StatusForeground = "#94a3b8";
                break;
        }

        TaskStatus = status;   // 触发派生属性精准通知
    }

    private void ApplyLinkState(DeviceLinkState state)
    {
        LinkState = state;
        (LinkBrush, LinkText) = state switch
        {
            DeviceLinkState.Verifying => ("#f59e0b", "识别中"),
            DeviceLinkState.Bound => ("#22c55e", "已连接"),
            DeviceLinkState.Rejected => ("#ef4444", "拒绝连接"),
            _ => ("#cbd5e1", "空闲")
        };
    }

    private void UpdatePriorityPresentation()
    {
        AccentBrush = IsEmergency ? "#ef4444" : "#2563eb";
        CardBackground = IsEmergency ? "#fef2f2" : "White";
        PriorityButtonText = IsEmergency ? "取消优先" : "优先";
        PriorityButtonBrush = IsEmergency ? "#ef4444" : "#ea580c";
    }

    private void SetHint(string text, string brush)
    {
        HintText = text;
        HintBrush = brush;
        HasHint = !string.IsNullOrWhiteSpace(text);
    }

    private void ClearHint()
    {
        HintText = string.Empty;
        HasHint = false;
    }

    private void NotifyCommands()
    {
        if (_lastCanPause != CanPause) { _lastCanPause = CanPause; PauseCommand.NotifyCanExecuteChanged(); }
        if (_lastCanResume != CanResume) { _lastCanResume = CanResume; ResumeCommand.NotifyCanExecuteChanged(); }
        if (_lastCanCancel != CanCancel) { _lastCanCancel = CanCancel; CancelCommand.NotifyCanExecuteChanged(); }
        if (_lastCanRetry != CanRetry) { _lastCanRetry = CanRetry; RetryCommand.NotifyCanExecuteChanged(); }
        if (_lastCanPriority != CanPriority) { _lastCanPriority = CanPriority; PriorityCommand.NotifyCanExecuteChanged(); }

        if (_lastHasTask != HasTask) { _lastHasTask = HasTask; DetailsCommand.NotifyCanExecuteChanged(); }
    }

    private static string ProtocolText(ProtocolType protocol) => protocol switch
    {
        ProtocolType.Mtp => "MTP",
        ProtocolType.PrivateSdk => "私有SDK",
        _ => "UMS"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:F1} {units[unit]}";
    }

    #endregion

    #region 派生属性精准通知

    partial void OnTaskStatusChanged(CollectTaskStatus? oldValue, CollectTaskStatus? newValue)
    {
        if ((oldValue == CollectTaskStatus.Scanning) != (newValue == CollectTaskStatus.Scanning))
            OnPropertyChanged(nameof(IsScanning));

        if ((oldValue == CollectTaskStatus.Collecting) != (newValue == CollectTaskStatus.Collecting))
            OnPropertyChanged(nameof(IsCollecting));

        if ((oldValue == CollectTaskStatus.Paused) != (newValue == CollectTaskStatus.Paused))
            OnPropertyChanged(nameof(IsPaused));

        if ((oldValue == CollectTaskStatus.Failed) != (newValue == CollectTaskStatus.Failed))
            OnPropertyChanged(nameof(IsFailedState));

        if ((oldValue == CollectTaskStatus.Completed) != (newValue == CollectTaskStatus.Completed))
            OnPropertyChanged(nameof(IsCompletedState));

        if ((oldValue == CollectTaskStatus.Created) != (newValue == CollectTaskStatus.Created))
            OnPropertyChanged(nameof(IsPendingState));
    }

    partial void OnTaskIdChanged(long? value) => OnPropertyChanged(nameof(HasTask));

    #endregion
}
