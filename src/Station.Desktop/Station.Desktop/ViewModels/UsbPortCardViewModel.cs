using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Collecting.Dtos;
using Station.Application.UsbPortCards;
using Station.Contracts;
using Station.Desktop.Presentation;
using Station.Domain.Collecting;

namespace Station.Desktop.ViewModels;

/// <summary>
/// 工作台 30 路 USB 采集通道卡片：实时反映该端口当前任务（采集中/已暂停/空闲），
/// 提供暂停/恢复/取消/重试/查看明细操作。
///
/// 单卡：物理端口固定占位，反映该端口当前任务状态。
/// 变更点：
///   - 新增 SlotIndex（0-based，卡片的稳定身份）
///   - 新增 CanRetryIdentify / RetryIdentifyCommand（识别拒绝后可重试）
///   - 新增 IsUnmapped / UnmappedText（Fixed 模式下未映射物理端口）
///   - 版本号仲裁（_lastTaskVersion / _lastTaskId）
///   - 设备断开后 3 秒延迟清空（_offlineClearTimer）
/// </summary>
public partial class UsbPortCardViewModel : ObservableObject, IDisposable
{
    private const int OfflineClearDelayMs = 3000;

    private bool _lastCanPause, _lastCanResume, _lastCanCancel,
                 _lastCanRetry, _lastCanPriority, _lastHasTask, _lastCanRetryIdentify;
    private double _lastSpeedMb = -1;

    private long _lastTaskVersion;
    private long _lastTaskId;

    private readonly DispatcherTimer _offlineClearTimer;

    /// <summary>卡片归还槽位的回调（清空定时器到点后触发）。</summary>
    private readonly Action<int> _onSlotReleased;
    // -------- 字段：保存原始端口号，用于 ClearUnmapped 恢复 --------
    private readonly int _portNumber;

    #region 属性

    /// <summary>0-based 槽位索引，卡片稳定身份。</summary>
    public int SlotIndex { get; }

    /// <summary>端口显示号（1-based，即 "#01" 里的 1）。</summary>
    public int PortNumber { get; }

    [ObservableProperty] private string _portText;

    [ObservableProperty] private string? _deviceKey;

    // 设备维度
    [ObservableProperty] private DeviceLinkState _linkState = DeviceLinkState.Offline;
    [ObservableProperty] private string _linkBrush = "#cbd5e1";
    [ObservableProperty] private string _linkText = "空闲";
    [ObservableProperty] private string _deviceText = "-- 等待设备连接";
    [ObservableProperty] private string _deviceSpaceText = string.Empty;
    [ObservableProperty] private bool _hasDeviceSpace;

    /// <summary>操作人显示文本（如 "张三 / 技术部"）。</summary>
    [ObservableProperty] private string _operatorText = string.Empty;

    /// <summary>是否有操作人信息（控制行 2 显示）。</summary>
    [ObservableProperty] private bool _hasOperator;

    // 任务维度
    [ObservableProperty] private CollectTaskStatus? _taskStatus;
    [ObservableProperty] private long? _taskId;
    [ObservableProperty] private string _taskNo = string.Empty;
    [ObservableProperty] private string _statusText = "--";
    [ObservableProperty] private string _statusBadgeBrush = "#f1f5f9";
    [ObservableProperty] private string _statusForeground = "#94a3b8";
    [ObservableProperty] private string _storageText = "--";

    // 提示
    [ObservableProperty] private string _hintText = string.Empty;
    [ObservableProperty] private string _hintBrush = "#b91c1c";
    [ObservableProperty] private bool _hasHint;

    // 展示
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

    /// <summary>识别拒绝时可重试。</summary>
    [ObservableProperty] private bool _canRetryIdentify;

    /// <summary>Fixed 模式下未映射物理端口。</summary>
    [ObservableProperty] private bool _isUnmapped;

    /// <summary>未映射提示文本。</summary>
    [ObservableProperty] private string _unmappedText = string.Empty;

    // 派生
    public bool IsScanning => TaskStatus == CollectTaskStatus.Scanning;
    public bool IsCollecting => TaskStatus == CollectTaskStatus.Collecting;
    public bool IsPaused => TaskStatus == CollectTaskStatus.Paused;
    public bool IsFailedState => TaskStatus == CollectTaskStatus.Failed;
    public bool IsCompletedState => TaskStatus == CollectTaskStatus.Completed;
    public bool IsPendingState => TaskStatus == CollectTaskStatus.Created;
    public bool HasTask => TaskId is not null;

    // 命令
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand ResumeCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RetryCommand { get; }
    public IRelayCommand PriorityCommand { get; }
    public IRelayCommand DetailsCommand { get; }
    public IRelayCommand RetryIdentifyCommand { get; }

    #endregion

    public UsbPortCardViewModel(
        int slotIndex,
        int portNumber,
        Action<UsbPortCardViewModel> pause,
        Action<UsbPortCardViewModel> resume,
        Action<UsbPortCardViewModel> cancel,
        Action<UsbPortCardViewModel> retry,
        Action<UsbPortCardViewModel> priority,
        Action<UsbPortCardViewModel> details,
        Action<UsbPortCardViewModel> retryIdentify,
        Action<int> onSlotReleased)
    {
        SlotIndex = slotIndex;
        _portNumber = portNumber;
        PortText = $"#{portNumber:D2}";
        _onSlotReleased = onSlotReleased;

        PauseCommand = new RelayCommand(() => pause(this), () => CanPause);
        ResumeCommand = new RelayCommand(() => resume(this), () => CanResume);
        CancelCommand = new RelayCommand(() => cancel(this), () => CanCancel);
        RetryCommand = new RelayCommand(() => retry(this), () => CanRetry);
        PriorityCommand = new RelayCommand(() => priority(this), () => CanPriority);
        DetailsCommand = new RelayCommand(() => details(this), () => HasTask);
        RetryIdentifyCommand = new RelayCommand(() => retryIdentify(this), () => CanRetryIdentify);

        _offlineClearTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(OfflineClearDelayMs)
        };
        _offlineClearTimer.Tick += (_, _) =>
        {
            _offlineClearTimer.Stop();
            ApplyTaskIdle();
            _onSlotReleased(SlotIndex);
        };
    }

    #region 快照 & 增量

    /// <summary>从快照 DTO 更新卡片（设备维度 + 任务维度，含版本仲裁）。</summary>
    public void UpdateFromDto(UsbPortCardDto dto)
    {
        // ============ 设备维度 ============
        if (!dto.IsConnected)
        {
            if (DeviceKey is not null || LinkState != DeviceLinkState.Offline)
            {
                // 不直接离线，交给 SetDeviceOffline 走 3 秒延迟
                SetDeviceOffline();
            }
            SetDeviceSpace(null, null);
            if (TaskId is null) return;
        }
        else
        {
            DeviceKey = dto.DeviceKey;
            if (ShouldApplySnapshotLinkState(LinkState, dto.LinkState))
                ApplyLinkState(dto.LinkState); ;

            if (TaskId is null)
            {
                DeviceText = dto.DeviceName ?? "设备已连接";
                MetaText = dto.Protocol is { } p ? $"{ProtocolText(p)} · 等待采集" : "--";
            }
            Opacity = 1;
            SetDeviceSpace(dto.DeviceAvailableBytes, dto.DeviceTotalBytes);
            _offlineClearTimer.Stop();
        }

        // ============ 任务维度 ============
        if (dto.TaskId is null)
        {
            // 关键：快照无任务不再主动清空。任务终态保留到设备断开或新任务覆盖。
            return;
        }

        var sameTask = TaskId == dto.TaskId;
        var version = dto.Version;

        // 新任务重置版本
        if (!sameTask)
        {
            _lastTaskVersion = 0;
            _lastTaskId = dto.TaskId.Value;
            _offlineClearTimer.Stop();
        }

        // 版本仲裁：新快照版本严格小于已记录版本 → 丢弃
        if (version > 0 && sameTask && version < _lastTaskVersion) return;
        if (version > 0) _lastTaskVersion = version;

        // 操作人：任务 ID 变化或第一次拿到用户名时写入
        if (!sameTask || string.IsNullOrEmpty(OperatorText))
        {
            var name = dto.OperatorName;
            var dept = dto.OperatorDeptName;

            if (!string.IsNullOrWhiteSpace(name))
            {
                OperatorText = string.IsNullOrWhiteSpace(dept)
                    ? name
                    : $"{name} / {dept}";
                HasOperator = true;
            }
            else
            {
                OperatorText = string.Empty;
                HasOperator = false;
            }
        }

        // 计算进度
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
            dto.SpeedBytesPerSecond, writeProgress);
    }

    /// <summary>高频进度更新（含去噪）。</summary>
    public void UpdateProgress(double percent, double speedBytesPerSecond)
    {
        var p = Math.Clamp(percent, 0, 100);
        if (Math.Abs(Progress - p) >= 0.05)
        {
            Progress = Math.Round(p, 1);
            var text = $"{Progress:F0}%";
            if (!string.Equals(ProgressText, text, StringComparison.Ordinal))
                ProgressText = text;
        }

        var mb = speedBytesPerSecond > 0 ? Math.Round(speedBytesPerSecond / 1048576d, 1) : 0d;
        if (Math.Abs(_lastSpeedMb - mb) >= 0.1)
        {
            _lastSpeedMb = mb;
            SpeedText = mb > 0 ? $"{mb:F1} MB/s" : "-- MB/s";
        }
    }

    /// <summary>状态 / 紧急更新（事件驱动，含版本仲裁）。</summary>
    public void UpdateStatus(CollectTaskStatus status, bool isEmergency, long version = 0)
    {
        // 事件驱动的任务状态通常不带 TaskId；由 Workbench 路由保证定位正确。
        // 版本仲裁：version 严格小于已记录 → 丢弃（version=0 表示兼容模式，直接接受）
        if (version > 0)
        {
            if (version < _lastTaskVersion) return;
            _lastTaskVersion = version;
        }

        // 新任务状态：清掉延迟清空定时器
        _offlineClearTimer.Stop();

        var emergencyChanged = IsEmergency != isEmergency;
        IsEmergency = isEmergency;
        ApplyStatusStyle(status);
        if (emergencyChanged) UpdatePriorityPresentation();
        NotifyCommands();
    }

    /// <summary>绑定任务 ID（识别完成后由 Workbench 调用）。</summary>
    public void BindTask(long taskId)
    {
        _lastTaskId = taskId;
        _lastTaskVersion = 0;
        TaskId = taskId;
        _offlineClearTimer.Stop();
    }

    #endregion

    #region 设备事件

    public void SetDeviceOnline(string key, string name, ProtocolType protocol)
    {
        _offlineClearTimer.Stop();
        DeviceKey = key;
        DeviceText = name;
        MetaText = $"{ProtocolText(protocol)} · 等待采集";
        ApplyLinkState(DeviceLinkState.Verifying);
        ClearHint();
        Opacity = 1;
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

    /// <summary>未映射物理端口：Fixed 模式下不占用固定槽位，临时落到此卡并提示。</summary>
    public void SetUnmapped(string? physicalPortId)
    {
        IsUnmapped = true;
        PortText = "#??";
        UnmappedText = string.IsNullOrEmpty(physicalPortId)
            ? "未识别物理端口，请配置端口映射"
            : $"未映射端口：{physicalPortId}（请配置映射表）";
    }

    public void ClearUnmapped()
    {
        if (!IsUnmapped) return;
        IsUnmapped = false;
        PortText = $"#{_portNumber:D2}";
        UnmappedText = string.Empty;
    }

    /// <summary>设备拔出：链路离线；无任务立即归零；有任务 3 秒后归零。</summary>
    public void SetDeviceOffline()
    {
        ApplyLinkState(DeviceLinkState.Offline);
        DeviceKey = null;

        SetDeviceSpace(null, null);
        NotifyCommands();

        if (TaskId is null)
        {
            DeviceText = "-- 等待设备连接";
            MetaText = "--";
            ApplyTaskIdle();
            Opacity = 0.55;
            _onSlotReleased(SlotIndex);
            return;
        }

        // 有任务：保留终态 3 秒后归零
        _offlineClearTimer.Stop();
        _offlineClearTimer.Start();
    }

    /// <summary>整体归零（初始化 / 复位用）。</summary>
    public void SetIdle()
    {
        _offlineClearTimer.Stop();
        ClearUnmapped();
        DeviceKey = null;
        ApplyLinkState(DeviceLinkState.Offline);
        DeviceText = "-- 等待设备连接";
        MetaText = "--";
        Opacity = 0.55;
        ApplyTaskIdle();
        SetDeviceSpace(null, null);
        ClearHint();
    }

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

    #endregion
   
    #region 内部

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

        if (isNewTask) DeviceText = recorderName;

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

            var mb = speed > 0 ? Math.Round(speed / 1048576d, 1) : 0d;
            _lastSpeedMb = mb;
            SpeedText = mb > 0 ? $"{mb:F1} MB/s" : "-- MB/s";
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
        _lastTaskVersion = 0;
        _lastTaskId = 0;

        TaskStatus = null;
        StatusText = "--";
        StatusBadgeBrush = "#f1f5f9";
        StatusForeground = "#94a3b8";

        OperatorText = string.Empty;
        HasOperator = false;

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
        CanRetryIdentify = false;
        NotifyCommands();
    }

    private void ApplyStatusStyle(CollectTaskStatus status)
    {
        if (TaskStatus == status) return;

        var p = UiStateMap.For(status);

        StatusText = p.Text;
        StatusBadgeBrush = p.BadgeBrush;
        StatusForeground = p.Foreground;

        CanPause = p.CanPause;
        CanResume = p.CanResume;
        CanCancel = p.CanCancel;
        CanRetry = p.CanRetry;
        CanPriority = p.CanPriority;

        TaskStatus = status;
    }

    private void ApplyLinkState(DeviceLinkState state)
    {
        var p = UiStateMap.For(state);
        LinkState = state;
        LinkBrush = p.Brush;
        LinkText = p.Text;
        CanRetryIdentify = p.CanRetryIdentify;
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
        if (_lastCanRetryIdentify != CanRetryIdentify)
        {
            _lastCanRetryIdentify = CanRetryIdentify;
            RetryIdentifyCommand.NotifyCanExecuteChanged();
        }
    }

    private static string ProtocolText(ProtocolType p) => p switch
    {
        ProtocolType.Mtp => "MTP",
        ProtocolType.PrivateSdk => "私有SDK",
        _ => "UMS"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var v = (double)bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:F1} {units[u]}";
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
    /// <summary>
    /// 快照链路态应用规则。
    /// 
    /// 快照只携带物理态（Offline / Verifying）；Bound / Rejected 属业务态，由 UI 事件驱动。
    /// 
    /// 规则表：
    ///   快照 Offline   → 永远应用（设备确实断开）
    ///   快照 Verifying →
    ///       当前 Offline   → 应用（设备接入）
    ///       当前 Verifying → 应用（无变化）
    ///       当前 Bound     → 不应用（保持业务态）
    ///       当前 Rejected  → 不应用（保持业务态）
    /// </summary>
    private static bool ShouldApplySnapshotLinkState(
        DeviceLinkState current, DeviceLinkState snapshot)
    {
        if (snapshot == DeviceLinkState.Offline) return true;

        // snapshot == Verifying
        return current is DeviceLinkState.Offline or DeviceLinkState.Verifying;
    }
    public void Dispose() => _offlineClearTimer.Stop();
}
