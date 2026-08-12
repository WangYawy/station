using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Station.Application.Authentication;
using Station.Desktop.Application.Session;
using Station.Desktop.UI.Services;
using Station.Desktop.UI.ViewModels;

namespace Station.Desktop.UI.Views;

public partial class ShellWindow : Window
{
    private readonly ISessionManager _sessions;
    private readonly SessionIdleTracker _idleTracker;

    public ShellWindow()
    {
        InitializeComponent();

        var services = App.Services!;
        _sessions = services.GetRequiredService<ISessionManager>();
        _sessions.SessionChanged += OnSessionChanged;

        _idleTracker = new SessionIdleTracker(
            _sessions,
            services.GetRequiredService<AuthOptions>());
        _idleTracker.Attach(this);

        OnSessionChanged();
    }

    private void OnSessionChanged()
    {
        if (_sessions.Current is null)
        {
            ShowLogin();
        }
        else
        {
            ShowMain();
        }
    }

    private void ShowLogin()
    {
        var services = App.Services!;
        var viewModel = new LoginViewModel(
            services.GetRequiredService<IAuthenticationService>(),
            _sessions);
        MainContent.Content = new LoginView { DataContext = viewModel };
    }

    private void ShowMain()
    {
        var services = App.Services!;
        var viewModel = new MainWindowViewModel(
            _sessions,
            services.GetRequiredService<IAuthenticationService>());
        MainContent.Content = new MainView { DataContext = viewModel };
    }
}
