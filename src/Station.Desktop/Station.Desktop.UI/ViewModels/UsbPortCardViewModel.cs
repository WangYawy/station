using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Collecting;
using Station.Application.UsbPortCard;
using Station.Contracts;
using Station.Domain.Enums;

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
    /// <summary>
    /// 端口号
    /// </summary>
    public string PortText { get; }

    /// <summary>设备唯一标识（用于匹配设备事件）</summary>
    [ObservableProperty]
    private string? _deviceKey;

    // =================== 设备连接维度 ===================

    /// <summary>设备连接态（正交维度，与任务态独立渲染）</summary>
    [ObservableProperty]
    private DeviceLinkState _linkState = DeviceLinkState.Offline;

    /// <summary>设备指示灯颜色</summary>
    [ObservableProperty]
    private string _linkBrush = "#cbd5e1";

    /// <summary>设备状态短标签（显示在灯旁）</summary>
    [ObservableProperty]
    private string _linkText = "空闲";

    /// <summary>设备名称</summary>
    [ObservableProperty]
    private string _deviceText = "-- 等待设备连接";
    /// <summary>设备磁盘空间 可用/总</summary>
    [ObservableProperty] private string _deviceSpaceText = string.Empty;
    [ObservableProperty] private bool _hasDeviceSpace;

    // =================== 任务维度 ===================

    /// <summary>任务主状态（事实来源，null 表示无任务）</summary>
    [ObservableProperty]
    private CollectTaskStatus? _taskStatus;

    [ObservableProperty]
    private long? _taskId;

    [ObservableProperty]
    private string _taskNo = string.Empty;

    /// <summary>任务状态文本（显示用，与 TaskStatus 同步）</summary>
    [ObservableProperty]
    private string _statusText = "--";

    /// <summary>任务徽章背景色</summary>
    [ObservableProperty]
    private string _statusBadgeBrush = "#f1f5f9";

    /// <summary>任务徽章前景色</summary>
    [ObservableProperty]
    private string _statusForeground = "#94a3b8";
    /// <summary>任务文件状态</summary>
    [ObservableProperty]
    private string _storageText = "--";

    // ================== 提示维度（拒绝 / 授权 / 警告） ==================

    /// <summary>提示文本（拒绝原因 / 授权提示 / 警告），为空时 UI 隐藏</summary>
    [ObservableProperty] private string _hintText = string.Empty;
    /// <summary>提示颜色</summary>
    [ObservableProperty] private string _hintBrush = "#b91c1c";
    /// <summary>是否有提示</summary>
    [ObservableProperty] private bool _hasHint;

    // =================== 展示信息 ===================

    /// <summary>协议 / 采集模式</summary>
    [ObservableProperty]
    private string _metaText = "--";

    /// <summary>进度条</summary>
    [ObservableProperty]
    private double _progress;
    /// <summary>进度文本</summary>
    [ObservableProperty]
    private string _progressText = "0%";
    /// <summary>速度</summary>
    [ObservableProperty]
    private string _speedText = "-- MB/s";

    /// <summary>是否无任务（任务维度空闲）</summary>
    [ObservableProperty]
    private bool _isIdle = true;

    [ObservableProperty]
    private double _opacity = 0.55;
    /// <summary>任务是否紧急优先</summary>
    [ObservableProperty]
    private bool _isEmergency;

    [ObservableProperty]
    private string _accentBrush = "#2563eb";

    [ObservableProperty]
    private string _cardBackground = "White";

    [ObservableProperty]
    private bool _canPriority;

    [ObservableProperty]
    private string _priorityButtonText = "优先";

    [ObservableProperty]
    private string _priorityButtonBrush = "#ea580c";

    [ObservableProperty]
    private bool _canPause;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canRetry;

    // =================== 派生状态（唯一来源 = TaskStatus） ===================
    public bool IsSanning => TaskStatus == CollectTaskStatus.Scanning;

    public bool IsCollecting => TaskStatus == CollectTaskStatus.Collecting;

    public bool IsPaused => TaskStatus == CollectTaskStatus.Paused;

    public bool IsFailedState => TaskStatus == CollectTaskStatus.Failed;

    public bool IsCompletedState => TaskStatus == CollectTaskStatus.Completed;

    /// <summary>待开始（对应 CollectTaskStatus.Created）</summary>
    public bool IsPendingState => TaskStatus == CollectTaskStatus.Created;

    // =================== 命令 ===================

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
    /// <summary>查看明细命令</summary>
    public IRelayCommand DetailsCommand { get; }
    #endregion 属性

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
        DetailsCommand = new RelayCommand(() => details(this), () => TaskId is not null);
    }

    #region 公共更新方法
    /// <summary>
    /// 从完整快照 DTO 更新卡片（用于启动加载和兜底刷新）
    /// </summary>
    /// <summary>从完整快照 DTO 更新卡片（心跳兜底 / 启动加载）</summary>
    public void UpdateFromDto(UsbPortCardDto dto)
    {
        // 设备维度
        if (!dto.IsConnected)
        {
            DeviceKey = null;
            ApplyLinkState(DeviceLinkState.Offline);
            // 只有无任务时才清 DeviceText（有任务时保留任务记录的设备名）
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
            DeviceText = dto.DeviceName ?? "设备已连接";
            // 心跳不能覆盖 Verifying / Rejected（精确值由事件驱动提供）
            if (LinkState is DeviceLinkState.Offline)
            {
                MetaText = dto.Protocol is { } p ? $"{ProtocolText(p)} · 等待采集" : "--";
                ApplyLinkState(DeviceLinkState.Bound);
            }
            Opacity = 1;
            SetDeviceSpace(dto.DeviceAvailableBytes, dto.DeviceTotalBytes);
        }

        // 任务维度
        if (dto.TaskId is null)
        {
            ApplyTaskIdle();
        }
        else
        {
            ApplyTaskData(
                dto.TaskId.Value, dto.TaskNo ?? "", dto.DeviceName ?? "未知设备",
                dto.Protocol ?? ProtocolType.Ums, true, dto.IsEmergency,
                dto.Status ?? CollectTaskStatus.Created,
                dto.TotalFiles, dto.CollectedFiles,
                dto.TotalBytes, dto.CollectedBytes,
                dto.SpeedBytesPerSecond);
        }
    }
    /// <summary>增量更新：进度和速度（高频入口，含去噪）</summary>
    public void UpdateProgress(double progressPercent, double speedBytesPerSecond)
    {
        // 进度：仅在 ≥0.05 变化时写字段（消除浮点抖动）
        var p = Math.Clamp(progressPercent, 0, 100);
        if (Math.Abs(Progress - p) >= 0.05)
        {
            Progress = Math.Round(p, 1);

            // ProgressText 仅在整数变化时刷新
            var newText = $"{Progress:F0}%";
            if (!string.Equals(ProgressText, newText, StringComparison.Ordinal))
                ProgressText = newText;
        }

        // 速度：仅在 ≥0.1 MB/s 变化时刷新
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
        IsEmergency = isEmergency;
        ApplyStatusStyle(status);
        // 紧急标记可能伴随按钮文案/颜色变化
        UpdatePriorityPresentation();
        NotifyCommands();
    }

    /// <summary>设备接入：进入"识别中"链路态</summary>
    public void SetDeviceOnline(string key, string name, ProtocolType protocol)
    {
        DeviceKey = key;
        DeviceText = name;
        MetaText = $"{protocol} · 等待采集";
        ApplyLinkState(DeviceLinkState.Verifying);

        // 设备插入时任务维度应已为空
        if (TaskId is not null)
            ApplyTaskIdle();

        ClearHint();  // 新设备接入，清掉旧提示
        Opacity = 1;
    }

    /// <summary>设备拔出：链路态回 Offline；无任务时任务维度一并归零</summary>
    public void SetDeviceOffline()
    {
        ApplyLinkState(DeviceLinkState.Offline);
        DeviceKey = null;
        // 只有无任务才清设备名与元数据，有任务时保留
        if (TaskId is null)
        {
            DeviceText = "-- 等待设备连接";
            MetaText = "--";
            ApplyTaskIdle();
            Opacity = 0.55;
        }
        SetDeviceSpace(null, null);
        // 有任务时保留进度显示（终态由 TaskStatusChanged 事件刷新）
        NotifyCommands();
    }

    /// <summary>整体归零（设备拔出 + 任务结束）</summary>
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
    /// <summary>绑定成功：链路态转 Bound</summary>
    public void SetBound()
    {
        ApplyLinkState(DeviceLinkState.Bound);
        ClearHint();
    }

    /// <summary>绑定失败：链路态转 Rejected</summary>
    public void SetRejected(string reason)
    {
        ApplyLinkState(DeviceLinkState.Rejected);
        SetHint(reason, "#b91c1c");   // 红
    }

    /// <summary>授权/警告类提示（可选，供后续扩展使用）</summary>
    public void SetWarning(string message)
        => SetHint(message, "#b45309");   // 橙

    /// <summary>设备磁盘空间（由心跳或事件更新）</summary>
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

    /// <summary>从任务 DTO 更新</summary>
    public void UpdateFromTask(CollectTaskDto task)
    {
        ApplyTaskData(
            task.TaskId, task.TaskNo, task.RecorderName, task.Protocol,
            task.IsAuto, task.IsEmergency, task.Status,
            task.TotalFiles, task.CollectedFiles,
            task.TotalBytes, task.CollectedBytes,
            task.SpeedBytesPerSecond);
    }

    #endregion

    #region 内部辅助方法

    /// <summary>
    /// 应用采集任务信息
    /// </summary>
    /// <summary>应用任务数据（任务维度核心入口）</summary>
    private void ApplyTaskData(
        long taskId, string taskNo, string recorderName, ProtocolType protocol,
        bool isAuto, bool isEmergency, CollectTaskStatus status,
        int totalFiles, int collectedFiles,
        long totalBytes, long collectedBytes, double speed)
    {
        TaskId = taskId;
        TaskNo = taskNo;
        IsIdle = false;
        Opacity = 1;
        IsEmergency = isEmergency;

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

        var speedMb = speed > 0 ? Math.Round(speed / 1048576d, 1) : 0d;
        _lastSpeedMb = speedMb;
        SpeedText = speedMb > 0 ? $"{speedMb:F1} MB/s" : "-- MB/s";

        ApplyStatusStyle(status);
        UpdatePriorityPresentation();
        NotifyCommands();
    }

    /// <summary>任务维度归零（无任务）</summary>
    private void ApplyTaskIdle()
    {
        TaskId = null;
        TaskNo = string.Empty;
        IsIdle = true;

        // 只写 TaskStatus，派生属性通过 OnTaskStatusChanged 通知
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

    /// <summary>应用任务状态（只动任务维度，不碰设备维度 / IsIdle）</summary>
    private void ApplyStatusStyle(CollectTaskStatus status)
    {
        CanPause = CanResume = CanCancel = CanRetry = CanPriority = false;

        switch (status)
        {
            case CollectTaskStatus.Scanning:
                StatusText = "扫描中";
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

        // 最后写 TaskStatus —— 触发派生属性的精准通知
        TaskStatus = status;
    }


    /// <summary>应用设备连接态（只动设备维度）</summary>
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

    /// <summary>根据 IsEmergency 刷新按钮文案/配色/背景</summary>
    private void UpdatePriorityPresentation()
    {
        AccentBrush = IsEmergency ? "#ef4444" : "#2563eb";
        CardBackground = IsEmergency ? "#fef2f2" : "White";
        PriorityButtonText = IsEmergency ? "取消优先" : "优先";
        PriorityButtonBrush = IsEmergency ? "#ef4444" : "#ea580c";
    }
    /// <summary>提示</summary>
    private void SetHint(string text, string brush)
    {
        HintText = text;
        HintBrush = brush;
        HasHint = !string.IsNullOrWhiteSpace(text);
    }
    /// <summary>清空提示</summary>
    private void ClearHint()
    {
        HintText = string.Empty;
        HasHint = false;
    }

    /// <summary>命令可用性精准通知（只在变化时触发）</summary>
    private void NotifyCommands()
    {
        if (_lastCanPause != CanPause) { _lastCanPause = CanPause; PauseCommand.NotifyCanExecuteChanged(); }
        if (_lastCanResume != CanResume) { _lastCanResume = CanResume; ResumeCommand.NotifyCanExecuteChanged(); }
        if (_lastCanCancel != CanCancel) { _lastCanCancel = CanCancel; CancelCommand.NotifyCanExecuteChanged(); }
        if (_lastCanRetry != CanRetry) { _lastCanRetry = CanRetry; RetryCommand.NotifyCanExecuteChanged(); }
        if (_lastCanPriority != CanPriority) { _lastCanPriority = CanPriority; PriorityCommand.NotifyCanExecuteChanged(); }

        var hasTask = TaskId is not null;
        if (_lastHasTask != hasTask) { _lastHasTask = hasTask; DetailsCommand.NotifyCanExecuteChanged(); }
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
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:F1} {units[unit]}";
    }
    #endregion

    #region 派生属性精准通知（唯一入口 = TaskStatus 变化）

    /// <summary>
    /// 任务主状态变化 → 只通知受影响的派生属性。
    /// 取代旧的 OnStatusTextChanged / OnIsIdleChanged，避免 5×N 次冗余通知。
    /// </summary>
    partial void OnTaskStatusChanged(CollectTaskStatus? oldValue, CollectTaskStatus? newValue)
    {
        if ((oldValue == CollectTaskStatus.Scanning) != (newValue == CollectTaskStatus.Scanning))
            OnPropertyChanged(nameof(IsSanning));

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

    #endregion
}

/// <summary>设备物理/绑定连接态，与任务态正交</summary>
public enum DeviceLinkState
{
    Offline,     // 未插入或已拔出
    Verifying,   // 已插入，绑定校验中
    Bound,       // 已识别、待命
    Rejected     // 拒绝接入
}
