using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Station.Application.Collecting;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class WorkbenchView : UserControl
{
    /// <summary>卡片间距（px）。要和 DataTemplate 里 UsbPortCardView 的 Margin*2 一致</summary>
    private const double GridSpacing = 16;

    /// <summary>容器四周留白</summary>
    private const double OuterPadding = 16;
    private ScrollViewer? _scroll;

    public WorkbenchView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RecalculateLayout();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindDescendantOfType<ScrollViewer>();
        if (_scroll is not null)
            _scroll.PropertyChanged += (_, args) =>
            {
                if (args.Property == ScrollViewer.ViewportProperty)
                    RecalculateLayout();
            };

        // 首帧布局完成后补算一次
        Dispatcher.UIThread.Post(RecalculateLayout, DispatcherPriority.Loaded);
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RecalculateLayout();
    }

    /// <summary>
    /// 根据当前 View 尺寸和 WindowModeOptions 中的 Rows/Columns，
    /// 均分可用空间得到每张卡片的宽高；不足 MinCardWidth/MinCardHeight 时取最小值。
    /// </summary>
    private void RecalculateLayout()
    {
        if (DataContext is not WorkbenchViewModel vm) return;
        if (vm.PortCards.Count == 0) return;

        // 关键：用 ScrollViewer 的 Viewport，而不是 UserControl 的 Bounds
        double availW, availH;
        if (_scroll is { } sv && sv.Viewport.Width > 0 && sv.Viewport.Height > 0)
        {
            availW = sv.Viewport.Width;
            availH = sv.Viewport.Height;
        }
        else
        {
            availW = Bounds.Width;
            availH = Bounds.Height;
        }
        if (availW <= 0 || availH <= 0) return;

        var cols = Math.Max(1, vm.Columns);
        var rows = Math.Max(1, vm.Rows);

        var innerW = availW - OuterPadding;
        var innerH = availH - OuterPadding;

        // 均分：扣掉间距后 ÷ 列/行数
        var cellW = (innerW - GridSpacing * (cols - 1)) / cols;
        var cellH = (innerH - GridSpacing * (rows - 1)) / rows;

        // 小于最小值则取最小值（WrapPanel 会自动换行，不会挤压边框）
        vm.CardWidth = Math.Max(vm.MinCardWidth, cellW);
        vm.CardHeight = Math.Max(vm.MinCardHeight, cellH);

        // 给 UniformGrid 一个确定总高度，它才能均分行高
        vm.GridHeight = rows * vm.CardHeight + GridSpacing * (rows - 1) + OuterPadding;
    }
}
