// 说明：与业务无关的可复用分页组件。任何列表页 VM 只要持有一个实例并把「加载某一页」的
//       回调交给它，就自动拥有：页码状态、上下页/首页/末页/跳转、每页条数、加载态、
//       首尾页按钮自动禁用、快速翻页竞态保护。

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Station.Desktop.ViewModels;

/// <summary>一次分页加载后的状态快照，由加载回调返回。</summary>
/// <param name="PageIndex">实际的当前页码（后端 clamp 后）。</param>
/// <param name="PageSize">实际的每页条数。</param>
/// <param name="Total">总记录数。</param>
public sealed record PageState(int PageIndex, int PageSize, int Total);

/// <summary>
/// 通用分页控制器。<br/>
/// 用法：<c>var pager = new PaginationViewModel { Loader = LoadPageAsync };</c><br/>
/// <c>LoadPageAsync</c> 负责查数据、填充列表、返回状态；返回 null 表示失败（此时分页状态保持不变）。
/// </summary>
public sealed partial class PaginationViewModel : ObservableObject
{
    private const int DefaultPageSize = 20;

    /// <summary>搜索序号：只认最后一次请求的结果，避免快速翻页时旧结果覆盖新结果。</summary>
    private int _seq;

    private bool _suppressReload;

    public PaginationViewModel(int pageSize = DefaultPageSize)
    {
        _pageSize = pageSize;
    }

    /// <summary>加载指定页的回调，返回状态快照；返回 null 表示本次加载失败。</summary>
    public Func<int, Task<PageState?>>? Loader { get; set; }

    /// <summary>每页条数选项。</summary>
    public ObservableCollection<int> PageSizeOptions { get; } = [15, 20, 50, 100];

    // ---------- 状态 ----------

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private int _pageIndex = 1;

    [ObservableProperty] private int _pageSize = DefaultPageSize;

    [ObservableProperty] private int _total;

    [ObservableProperty] private int _totalPages = 1;

    [ObservableProperty] private bool _hasPrevious;

    [ObservableProperty] private bool _hasNext;

    /// <summary>页码输入框的值（NumericUpDown.Value 是 decimal?）。</summary>
    [ObservableProperty] private decimal? _pageInput = 1;

    /// <summary>页码输入框上限，TotalPages 为 0 时兜底为 1，避免锁死输入。</summary>
    [ObservableProperty] private decimal _maxPage = 1;

    [ObservableProperty] private string _summaryText = string.Empty;

    // ---------- 外观开关 ----------

    [ObservableProperty] private bool _showPageSizeSelector = true;

    [ObservableProperty] private bool _showQuickJump = true;

    [ObservableProperty] private bool _showRefresh;

    // ---------- 对外方法 ----------
    /// <summary>
    /// 在 UI 线程执行一段代码。加载回调里填充 ObservableCollection、或改任何会被绑定的属性时用它。<br/>
    /// 已在 UI 线程则同步执行；没有 UI 线程可用（如单元测试）则退化为直接执行，不抛异常。
    /// </summary>
    public Task RunOnUiThreadAsync(Action action)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();
        }
        catch (Exception)
        {
            action();               // 无 UI 线程（如单元测试）时退化为直接执行，不炸
            return Task.CompletedTask;
        }
    }

    /// <summary>主动加载指定页（点「查询」、初始化时用）。</summary>
    public Task LoadAsync(int page = 1) => GoAsync(page);

    /// <summary>直接套用一次状态（不走 Loader，适合外部已自行加载完的场景）。</summary>
    public void Apply(PageState state)
    {
        PageIndex = state.PageIndex < 1 ? 1 : state.PageIndex;
        if (state.PageSize >= 1)
        {
            PageSize = state.PageSize;
        }

        Total = state.Total;
        TotalPages = PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;
        HasPrevious = PageIndex > 1;
        HasNext = PageIndex < TotalPages;
        PageInput = PageIndex;
        MaxPage = Math.Max(1, TotalPages);
        SummaryText = Total == 0
            ? string.Empty
            : $"共 {Total} 条 · 每页 {PageSize} 条 · 第 {PageIndex}/{TotalPages} 页";
    }

    /// <summary>
    /// 清空到未加载状态（重置查询条件、切换页面时用）。<br/>
    /// 全程抑制自动重载，避免改 PageSize 时先发一次多余请求。
    /// </summary>
    /// <param name="pageSize">同时把每页条数重置为指定值；null 表示不改。</param>
    public void Reset(int? pageSize = null)
    {
        _suppressReload = true;
        try
        {
            if (pageSize.HasValue && pageSize.Value >= 1)
            {
                PageSize = pageSize.Value;
            }

            PageIndex = 1;
            Total = 0;
            TotalPages = 1;
            HasPrevious = false;
            HasNext = false;
            PageInput = 1;
            MaxPage = 1;
            SummaryText = string.Empty;
        }
        finally
        {
            _suppressReload = false;
        }
    }

    // ---------- 命令 ----------

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private Task FirstPageAsync() => GoAsync(1);

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private Task PrevPageAsync() => GoAsync(PageIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextPageAsync() => GoAsync(PageIndex + 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task LastPageAsync() => GoAsync(TotalPages);

    [RelayCommand]
    private Task GoToPageAsync() => GoAsync((int)(PageInput ?? 1));

    [RelayCommand]
    private Task RefreshAsync() => GoAsync(PageIndex);

    private bool CanGoPrev => !IsLoading && HasPrevious;

    private bool CanGoNext => !IsLoading && HasNext;

    // ---------- 内部 ----------

    /// <summary>每页条数变化：回到第 1 页重载（构造期的默认值赋值不会触发）。</summary>
    partial void OnPageSizeChanged(int value)
    {
        if (_suppressReload)
        {
            return;
        }

        _ = GoAsync(1);
    }

    private async Task GoAsync(int page)
    {
        var loader = Loader;
        if (loader is null || _suppressReload)
        {
            return;
        }

        page = Math.Max(1, page);

        var seq = ++_seq;

        await RunOnUiThreadAsync(() => { IsLoading = true; RefreshCommands(); });

        try
        {
            var state = await loader(page);
            if (seq != _seq)
            {
                return; // 已有更新的请求，丢弃本次结果
            }

            if (state is not null)
            {
                Apply(state);
            }
        }
        finally
        {
            if (seq == _seq)
            {
                await RunOnUiThreadAsync(() => { IsLoading = false; RefreshCommands(); });
            }
        }
    }

    private void RefreshCommands()
    {
        FirstPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }
}
