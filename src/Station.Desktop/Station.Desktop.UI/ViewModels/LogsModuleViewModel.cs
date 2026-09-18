using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Audit;

namespace Station.Desktop.ViewModels;

/// <summary>结果状态下拉项。</summary>
public sealed record ResultOption(bool? Value, string Text);

/// <summary>
/// 日志中心：审计/操作日志（登录、配置修改、擦除、导入导出等）。<br/>
/// 触发策略：仅「查询」「重置」与分页条上的翻页会发起请求，条件变更不自动请求。
/// </summary>
public partial class LogsModuleViewModel : ObservableObject, IDisposable
{
    private const string AllTypeText = "全部";

    private readonly IAuditLogService _audit;

    /// <summary>列表数据。整体替换而非 Clear+Add，避免后台线程发 CollectionChanged 导致 DataGrid 不重绘。</summary>
    [ObservableProperty] private ObservableCollection<AuditLogDto> _logs = [];

    public ObservableCollection<string> OperationTypes { get; } = [AllTypeText];

    public ObservableCollection<ResultOption> ResultOptions { get; } =
        [new(null, "全部"), new(true, "成功"), new(false, "失败")];

    /// <summary>分页控制器，View 里直接 DataContext 给 PaginationBar。</summary>
    public PaginationViewModel Pager { get; }

    // ---------- 状态 ----------

    [ObservableProperty] private string _message = string.Empty;

    [ObservableProperty] private bool _isEmpty;

    // ---------- 查询条件 ----------

    [ObservableProperty] private string _keyword = string.Empty;

    [ObservableProperty] private string _selectedType = AllTypeText;

    [ObservableProperty] private DateTime? _dateFrom;
    [ObservableProperty] private DateTime? _dateTo;

    [ObservableProperty] private ResultOption? _selectedResult;

    public LogsModuleViewModel(IAuditLogService audit)
    {
        _audit = audit;
        SelectedResult = ResultOptions[0];

        Pager = new PaginationViewModel(15)
        {
            Loader = LoadPageAsync,
            //ShowRefresh = true
        };

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var types = await _audit.GetOperationTypesAsync();
            foreach (var t in types)
            {
                if (!string.IsNullOrWhiteSpace(t) && !OperationTypes.Contains(t))
                {
                    OperationTypes.Add(t);
                }
            }
        }
        catch
        {
            // 类型下拉拉不到不影响主流程，保留“全部”即可
        }

        await Pager.LoadAsync(1);
    }

    // ---------- 命令 ----------

    /// <summary>点「查询」：条件生效并回到第 1 页。</summary>
    [RelayCommand]
    private Task SearchAsync() => Pager.LoadAsync(1);

    [RelayCommand]
    private void Reset()
    {
        Keyword = string.Empty;
        SelectedType = AllTypeText;
        DateFrom = null;
        DateTo = null;
        SelectedResult = ResultOptions[0];

        Pager.Reset(Pager.PageSize);          // 抑制自动重载，避免多打一次后端
        _ = Pager.LoadAsync(1);   // 只发这一次请求
    }

    // ---------- 加载 ----------

    /// <summary>加载指定页；返回 null 表示失败（分页状态保持不变）。</summary>
    private async Task<PageState?> LoadPageAsync(int page)
    {
        if (DateFrom.HasValue && DateTo.HasValue && DateFrom.Value.Date > DateTo.Value.Date)
        {
            Message = "开始日期不能晚于结束日期";
            return null;
        }

        Message = string.Empty;

        try
        {
            var result = await _audit.SearchAsync(
                keyword: Keyword,
                operationType: SelectedType == AllTypeText ? null : SelectedType,
                from: DateFrom?.Date,
                to: DateTo?.Date,
                success: SelectedResult?.Value,
                pageIndex: page,
                pageSize: Pager.PageSize);

            var rows = result.Items;
            //await Pager.RunOnUiThreadAsync(() =>
            //{
                Logs = new ObservableCollection<AuditLogDto>(rows);   // 整体替换，不是 Clear+Add
                IsEmpty = rows.Count == 0;
            //});

            return new PageState(result.PageIndex, result.PageSize, result.Total);
        }
        catch (Exception ex)
        {
            Message = $"加载日志失败：{ex.Message}";
            Logs.Clear();
            IsEmpty = true;
            return null;
        }
    }

    public void Dispose()
    {
    }
}
