using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Station.Desktop.UI.Views;

/// <summary>新报警弹窗：右下角置顶提示，6 秒自动关闭，点击跳转报警中心。</summary>
public partial class AlertPopupWindow : Window
{
    private readonly DispatcherTimer _autoClose;

    public AlertPopupWindow(string title, string detail, Window owner)
    {
        InitializeComponent();
        TitleText.Text = title;
        DetailText.Text = detail;
        Owner = owner;
        PointerPressed += (_, _) => Close();
        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoClose.Tick += (_, _) => Close();
        _autoClose.Start();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Screens?.ScreenFromWindow(this) is { } screen)
        {
            Position = new PixelPoint(
                screen.WorkingArea.Right - (int)Width - 20,
                screen.WorkingArea.Bottom - (int)Height - 20);
        }
    }
}
