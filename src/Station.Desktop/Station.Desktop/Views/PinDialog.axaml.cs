using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class PinDialog : Window
{
    private TaskCompletionSource<bool>? _tcs;
    private PinViewModel? _vm;

    public PinDialog()
    {
        InitializeComponent();
    }

    public PinDialog(PinViewModel viewModel) : this()
    {
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    /// <summary>模态显示，返回是否验证通过</summary>
    public Task<bool> ShowAsync(Window owner)
    {
        _tcs = new TaskCompletionSource<bool>();
        Closed += (_, _) => _tcs.TrySetResult(false);
        _ = ShowDialog(owner);
        return _tcs.Task;
    }

    private void OnCloseRequested(bool result)
    {
        _tcs?.TrySetResult(result);
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _tcs?.TrySetResult(false);
        Close();
    }

    // ============================================================
    // 物理键盘输入支持（数字键 / 退格 / Esc / Enter）
    // ============================================================
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;

        // 数字键 0-9
        var key = e.Key;
        if (key >= Key.D0 && key <= Key.D9)
        {
            _vm.AppendCommand.Execute(((char)('0' + (key - Key.D0))).ToString());
            e.Handled = true;
            return;
        }
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            _vm.AppendCommand.Execute(((char)('0' + (key - Key.NumPad0))).ToString());
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Back:
                _vm.BackspaceCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                _tcs?.TrySetResult(false);
                Close();
                e.Handled = true;
                break;

            case Key.Enter:
                _vm.SubmitCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm = null;
        }
        base.OnClosed(e);
    }
}
