using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.Settings;
using Station.Application.Uploading;
using Station.Desktop.Application.Monitoring;
using Station.Desktop.Application.OperationAccess;
using Station.Desktop.Application.Session;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Repositories;
using SqlSugar;
using Station.Domain.Repositories;

namespace Station.Desktop.UI.ViewModels;

/// <summary>
/// 工作台：USB 采集通道卡片（行数/每行卡片数/卡片宽高可配置）+ 统计 + 本机监控。
/// 卡片实时反映各端口当前任务（采集中/已暂停/空闲），支持暂停/恢复/取消/重试/查看明细。
/// </summary>
public partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly ICollectTaskService _collectService;
    private readonly IUploadService _uploadService;
    private readonly IRepository<CollectFile> _files;
    private readonly ILicenseService _license;
    private readonly IOperationAccessService _operationAccess;
    private readonly CollectOptions _collectOptions;
    private readonly WorkbenchOptions _workbench;
    private readonly SystemMonitorService _monitor;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, long> _portTaskIds = [];
    private int _monitorTicks;
    private int _licenseTicks;

    /// <summary>
    /// 端口卡片
    /// </summary>
    public ObservableCollection<UsbPortCardViewModel> PortCards { get; }

    [ObservableProperty]
    private int _columns = 5;

    [ObservableProperty]
    private double _cardWidth = 240;

    [ObservableProperty]
    private double _cardHeight = 200;

    [ObservableProperty]
    private string _onlineText = "加载中…";

    [ObservableProperty]
    private string _todayText = "—";

    [ObservableProperty]
    private string _pendingUploadText = "—";

    [ObservableProperty]
    private string _portStatsText = "采集中 0 · 已暂停 0 · 空闲 30";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _cpuText = "—";

    [ObservableProperty]
    private string _memText = "—";

    [ObservableProperty]
    private string _diskText = "—";

    [ObservableProperty]
    private string _netText = "—";

    [ObservableProperty]
    private string _portsText = "—";

    [ObservableProperty]
    private string _deviceText = "—";

    [ObservableProperty]
    private bool _canOperate;

    public WorkbenchViewModel(
        ISessionManager sessions,
        ICollectTaskService collectService,
        IUploadService uploadService,
        CollectOptions collectOptions,
        IRepository<CollectFile> files,
        ILicenseService license,
        IOperationAccessService operationAccess,
        WorkbenchOptions workbench)
    {
        _sessions = sessions;
        _collectService = collectService;
        _uploadService = uploadService;
        _files = files;
        _license = license;
        _operationAccess = operationAccess;
        _collectOptions = collectOptions;
        _workbench = workbench;
        _monitor = new SystemMonitorService(collectOptions);

        PortCards = [];
        EnsureCardLayout(); // 绘制卡片布局

        _sessions.SessionChanged += OnSessionChanged;
        OnSessionChanged();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) =>
        {
            if (++_monitorTicks % 2 == 0)
            {
                RefreshMonitor();  // 每2秒刷新一次状态栏
            }

            if (++_licenseTicks % 30 == 0)
            {
                await RefreshLicenseAsync(); // 每30秒刷新一次授权状态
            }

            if (_monitorTicks % 2 == 0)
            {
                await RefreshDataAsync(); // 每2秒刷新一次采集卡片数据
            }
        };
        _timer.Start();
        RefreshMonitor(); // 刷新状态栏
        _ = RefreshLicenseAsync();
        _ = RefreshDataAsync();
    }

    private void OnSessionChanged()
    {
        CanOperate = _sessions.IsAuthenticated &&
                     _operationAccess.HasPermission(_sessions.Current, "collect");
    }

    private void RefreshMonitor()
    {
        var lines = _monitor.Snapshot();
        CpuText = ValueOf(lines, 0);
        MemText = ValueOf(lines, 1);
        DiskText = ValueOf(lines, 2);
        NetText = ValueOf(lines, 3);
        PortsText = ValueOf(lines, 4);
        DeviceText = ValueOf(lines, 5);
    }

    private static string ValueOf(IReadOnlyList<MonitorLine> lines, int index) =>
        lines.Count > index ? lines[index].Value : "—";

    /// <summary>
    /// 刷新数据，构建定时任务&构建执行&紧急优先&
    /// </summary>
    /// <returns></returns>
    private async Task RefreshDataAsync()
    {
        try
        {
            EnsureCardLayout();
            var tasks = await _collectService.GetActiveTasksAsync();
            RefreshPorts(tasks);
            await RefreshTodayAsync();
            PendingUploadText = (await _uploadService.CountPendingUploadsAsync()).ToString();
        }
        catch
        {
            // 数据刷新失败不阻塞界面
        }
    }

    /// <summary>
    /// 刷新端口采集任务
    /// </summary>
    /// <param name="tasks">当前任务列表</param>
    private void RefreshPorts(IReadOnlyList<CollectTaskDto> tasks)
    {
        var byId = tasks.ToDictionary(t => t.TaskId, t => t);
        var collecting = 0;
        var paused = 0;

        // 1) 更新已占用端口：任务仍在运行则刷新，否则清空为空闲
        foreach (var (port, taskId) in _portTaskIds.ToList())
        {
            if (byId.TryGetValue(taskId, out var task))
            {
                PortCards[port - 1].UpdateFromTask(task);
                if (task.Status == CollectTaskStatus.Paused)
                {
                    paused++;
                }
                else
                {
                    collecting++;
                }
            }
            else
            {
                _portTaskIds.Remove(port);
                PortCards[port - 1].SetIdle();
            }
        }

        // 2) 新任务分配到第一个空闲端口（端口映射保持稳定）
        var assigned = _portTaskIds.Values.ToHashSet();
        foreach (var task in tasks.Where(t => !assigned.Contains(t.TaskId)))
        {
            var port = Enumerable.Range(1, PortCards.Count).FirstOrDefault(p => !_portTaskIds.ContainsKey(p));
            if (port == 0)
            {
                break;
            }

            _portTaskIds[port] = task.TaskId;
            PortCards[port - 1].UpdateFromTask(task);
            if (task.Status == CollectTaskStatus.Paused)
            {
                paused++;
            }
            else
            {
                collecting++;
            }
        }

        PortStatsText = $"采集中 {collecting} · 已暂停 {paused} · 空闲 {PortCards.Count - collecting - paused}";
    }

    /// <summary>按配置（行数/每行卡片数/宽高）重建卡片网格；配置变更时保留端口→任务映射。</summary>
    private void EnsureCardLayout()
    {
        var total = Math.Clamp(_workbench.Rows, 1, 10) * Math.Clamp(_workbench.Columns, 1, 10);
        var width = (double)Math.Clamp(_workbench.CardWidth, 120, 500);
        var height = (double)Math.Clamp(_workbench.CardHeight, 100, 400);
        if (PortCards.Count == total && Columns == _workbench.Columns && CardWidth == width && CardHeight == height)
        {
            return;
        }

        Columns = Math.Clamp(_workbench.Columns, 1, 10);
        CardWidth = width;
        CardHeight = height;

        var existing = PortCards.ToList();
        var rebuilt = new List<UsbPortCardViewModel>();
        for (var i = 1; i <= total; i++)
        {
            var card = i <= existing.Count ? existing[i - 1] : CreateCard(i);
            card.SetCardSize(width, height);
            rebuilt.Add(card);
        }

        foreach (var port in _portTaskIds.Keys.Where(p => p > total).ToList())
        {
            _portTaskIds.Remove(port);
        }

        PortCards.Clear();
        foreach (var card in rebuilt)
        {
            PortCards.Add(card);
        }
    }

    /// <summary>
    /// 创建新卡片
    /// </summary>
    /// <param name="portIndex">端口编号</param>
    /// <returns></returns>
    private UsbPortCardViewModel CreateCard(int portIndex) => new(
        portIndex,
        c => _ = OperateAsync(c, s => s.PauseAsync(c.TaskId!.Value)),
        c => _ = OperateAsync(c, s => s.ResumeAsync(c.TaskId!.Value)),
        c => _ = OperateAsync(c, s => s.CancelAsync(c.TaskId!.Value)),
        c => _ = OperateAsync(c, s => s.StartAsync(c.TaskId!.Value)),
        c => _ = ToggleEmergencyAsync(c),
        CardWidth,
        CardHeight);

    /// <summary>
    /// 紧急优先上传切换
    /// </summary>
    /// <param name="card">采集卡片</param>
    /// <returns></returns>
    private async Task ToggleEmergencyAsync(UsbPortCardViewModel card)
    {
        if (!CanOperate || card.TaskId is null)
        {
            return;
        }

        try
        {
            var target = !card.IsEmergency;
            var ok = await _collectService.SetEmergencyAsync(card.TaskId.Value, target);
            StatusMessage = ok
                ? target ? $"已设为紧急优先（上限 {_collectOptions.MaxEmergencyTasks}）" : "已取消紧急优先"
                : $"紧急优先已达上限（{_collectOptions.MaxEmergencyTasks}），请先取消其他优先任务";
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"操作失败：{ex.Message}";
        }
    }

    private async Task RefreshTodayAsync()
    {
        try
        {
            var today = DateTime.Today;
            var rows = (await _files.GetListAsync(f => f.Status == CollectFileStatus.Completed && f.CollectedAt >= today))
                .Select(f => new { f.Size }).ToList();
            TodayText = $"{rows.Count} 个 · {FormatSize(rows.Sum(r => r.Size))}";
        }
        catch
        {
            TodayText = "—";
        }
    }

    /// <summary>
    /// 刷新授权状态
    /// </summary>
    private async Task RefreshLicenseAsync()
    {
        try
        {
            var check = await _license.CheckAsync();
            OnlineText = check.Message;
        }
        catch
        {
            OnlineText = "授权状态未知";
        }
    }

    /// <summary>
    /// 操作
    /// </summary>
    /// <param name="card">采集卡片</param>
    /// <param name="action">执行动作命令</param>
    /// <returns></returns>
    private async Task OperateAsync(UsbPortCardViewModel card, Func<ICollectTaskService, Task> action)
    {
        if (!CanOperate || card.TaskId is null)
        {
            return;
        }

        try
        {
            await action(_collectService);
            await RefreshDataAsync();
        }
        catch
        {
            // 操作失败由采集服务写入状态
        }
    }

    /// <summary>
    /// 获取采集任务文件列表
    /// </summary>
    /// <param name="taskId">采集任务Id</param>
    /// <returns></returns>
    public async Task<IReadOnlyList<CollectFileDto>> GetTaskFilesAsync(long taskId) =>
        await _collectService.GetTaskFilesAsync(taskId);

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

    public void Dispose()
    {
        _sessions.SessionChanged -= OnSessionChanged;
        _timer.Stop();
    }
}
