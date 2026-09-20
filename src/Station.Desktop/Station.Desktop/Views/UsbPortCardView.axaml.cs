using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Station.Application.Collecting;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class UsbPortCardView : UserControl
{
    public UsbPortCardView()
    {
        InitializeComponent();
    }

    // 触屏长按 → 弹出上下文菜单
    private void OnCardHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started) return;

        // 长按开始时弹出 ContextMenu
        if (sender is Control control && control.ContextMenu is { } menu)
        {
            menu.Open(control);
        }
    }
}
