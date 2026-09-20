using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Audit;

namespace Station.Desktop.ViewModels;

public sealed record ResultOption(bool? Value, string Text);
public sealed record OperationTypeOption(string? Code, string DisplayName);
public sealed record OperatorOption(string? UserNo, string DisplayName);
public sealed record TimeRangePreset(string Key, string DisplayName);

/// <summary>
/// 日志中心。<br/>
/// 触发策略：仅「查询」「重置」「分页」发起请求；条件变更不自动请求。<br/>
/// 时间范围必选（默认"今天"），从源头杜绝全表扫。
/// </summary>
public partial class LogsModuleViewModel : ObservableObject, IDisposable
{
    private readonly IAuditLogService _audit;

    [ObservableProperty] private ObservableCollection<AuditLogDto> _logs = [];
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isEmpty;

    // ---------- 搜索条件 ----------

    [ObservableProperty] private TimeRangePreset? _selectedTimeRange;
    [ObservableProperty] private DateTime? _customDateFrom;
    [ObservableProperty] private DateTime? _customDateTo;
    [ObservableProperty] private OperationTypeOption? _selectedType;
    [ObservableProperty] private OperatorOption? _selectedOperator;
    [ObservableProperty] private ResultOption? _selectedResult;
    [ObservableProperty] private string _detailKeyword = string.Empty;

    /// <summary>是否显示自定义日期区（仅 SelectedTimeRange.Key == "custom" 时）。</summary>
    public bool IsCustomRange => SelectedTimeRange?.Key == "custom";

    // ---------- 下拉数据 ----------

    public ObservableCollection<TimeRangePreset> TimeRangePresets { get; } = [];
    public ObservableCollection<OperationTypeOption> OperationTypes { get; } = [];
    public ObservableCollection<OperatorOption> Operators { get; } = [];
    public ObservableCollection<ResultOption> ResultOptions { get; } = [];

    public PaginationViewModel Pager { get; }

    public LogsModuleViewModel(IAuditLogService audit)
    {
        _audit = audit;

        // 时间预设
        TimeRangePresets.Add(new("today", "今天"));
        TimeRangePresets.Add(new("last7", "近 7 天"));
        TimeRangePresets.Add(new("last30", "近 30 天"));
        TimeRangePresets.Add(new("thisMonth", "本月"));
        TimeRangePresets.Add(new("custom", "自定义"));
        SelectedTimeRange = TimeRangePresets[0];

        // 操作类型（词表零 IO）
        OperationTypes.Add(new(null, "全部"));
        foreach (var t in audit.GetOperationTypes())
            OperationTypes.Add(new(t.Code, t.DisplayName));
        SelectedType = OperationTypes[0];

        // 结果
        ResultOptions.Add(new(null, "全部"));
        ResultOptions.Add(new(true, "成功"));
        ResultOptions.Add(new(false, "失败"));
        SelectedResult = ResultOptions[0];

        // 操作人先占位，异步填充
        Operators.Add(new(null, "全部操作人"));
        SelectedOperator = Operators[0];

        Pager = new PaginationViewModel(15) { Loader = LoadPageAsync };

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // 先拉首页数据（快）
        await Pager.LoadAsync(1);
        // 再拉操作人（IUserService 可能慢，可放后台）
        _ = LoadOperatorsAsync();
    }

    private async Task LoadOperatorsAsync()
    {
        try
        {
            var list = await _audit.GetSelectableOperatorsAsync();
            await Pager.RunOnUiThreadAsync(() =>
            {
                Operators.Clear();
                Operators.Add(new(null, "全部操作人"));
                foreach (var o in list)
                    Operators.Add(new(o.UserNo, $"{o.UserName}（{o.UserNo}）"));
                SelectedOperator = Operators[0];
            });
        }
        catch
        {
            // 忽略：下拉拉不到不影响主流程
        }
    }

    // ---------- 命令 ----------

    [RelayCommand]
    private Task SearchAsync() => Pager.LoadAsync(1);

    /// <summary>重置 = 恢复默认条件（今天 / 全部 / 全部 / 全部 / 空详情）+ 回到第 1 页。</summary>
    [RelayCommand]
    private void Reset()
    {
        SelectedTimeRange = TimeRangePresets[0];
        CustomDateFrom = null;
        CustomDateTo = null;
        SelectedType = OperationTypes[0];
        SelectedOperator = Operators.Count > 0 ? Operators[0] : null;
        SelectedResult = ResultOptions[0];
        DetailKeyword = string.Empty;

        Pager.Reset(Pager.PageSize);
        _ = Pager.LoadAsync(1);
    }

    // ---------- 联动 ----------

    partial void OnSelectedTimeRangeChanged(TimeRangePreset? value)
    {
        OnPropertyChanged(nameof(IsCustomRange));
        if (value?.Key != "custom")
        {
            CustomDateFrom = null;
            CustomDateTo = null;
        }
    }

    // ---------- 加载 ----------

    private (DateTime? from, DateTime? to) ResolveRange()
    {
        var key = SelectedTimeRange?.Key ?? "today";
        var today = DateTime.Today;
        return key switch
        {
            "today" => (today, today),
            "last7" => (today.AddDays(-6), today),
            "last30" => (today.AddDays(-29), today),
            "thisMonth" => (new DateTime(today.Year, today.Month, 1), today),
            "custom" => (CustomDateFrom?.Date, CustomDateTo?.Date),
            _ => (today, today)
        };
    }

    private async Task<PageState?> LoadPageAsync(int page)
    {
        var (from, to) = ResolveRange();

        if (SelectedTimeRange?.Key == "custom")
        {
            if (from is null || to is null)
            {
                Message = "请选择开始和结束日期";
                return null;
            }
            if (from.Value.Date > to.Value.Date)
            {
                Message = "开始日期不能晚于结束日期";
                return null;
            }
        }

        Message = string.Empty;

        try
        {
            var result = await _audit.SearchAsync(
                from: from,
                to: to,
                operationType: SelectedType?.Code,
                operatorNo: SelectedOperator?.UserNo,
                success: SelectedResult?.Value,
                detailKeyword: DetailKeyword,
                pageIndex: page,
                pageSize: Pager.PageSize);

            var rows = result.Items;
            Logs = new ObservableCollection<AuditLogDto>(rows);
            IsEmpty = rows.Count == 0;
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

    public void Dispose() { }
}
