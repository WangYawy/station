using Avalonia.Controls;
using Avalonia.Interactivity;
using Station.Desktop.ViewModels;

namespace Station.Desktop.Views;

public partial class LoginDialog : Window
{
    private LoginViewModel? _viewModel;

    public LoginDialog()
    {
        InitializeComponent();
    }

    public LoginDialog(LoginViewModel viewModel, Window owner) : this()
    {
        Owner = owner;
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.LoggedIn += OnLoggedIn;
    }

    private void OnLoggedIn()
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LoggedIn -= OnLoggedIn;
        }

        base.OnClosed(e);
    }
}
