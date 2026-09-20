using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class ExitConfirmDialog : Window
{
    private TaskCompletionSource<bool>? _tcs;

    public ExitConfirmDialog()
    {
        InitializeComponent();
    }

    public ExitConfirmDialog(ExitConfirmViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not ExitConfirmViewModel vm) return;

        switch (e.Key)
        {
            case Key.Enter:
                vm.ConfirmCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _tcs?.TrySetResult(false);
                Close();
                e.Handled = true;
                break;
        }
    }
}
