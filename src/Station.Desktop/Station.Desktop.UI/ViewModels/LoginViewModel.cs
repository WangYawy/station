using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Authentication;
using Station.Desktop.Application.Session;

namespace Station.Desktop.UI.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly ISessionManager _sessions;

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isBusy;

    public LoginViewModel(IAuthenticationService authentication, ISessionManager sessions)
    {
        _authentication = authentication;
        _sessions = sessions;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        HasError = false;
        IsBusy = true;
        try
        {
            var result = await _authentication.LoginAsync(new LoginRequest(UserName.Trim(), Password, "desktop"));
            if (result.Success && result.Session is not null)
            {
                Password = string.Empty;
                _sessions.Start(result.Session);
                return;
            }

            ErrorMessage = result.FailureReason switch
            {
                LoginFailureReason.Disabled => "账号已禁用，请联系管理员",
                LoginFailureReason.LockedOut when result.LockedUntil is { } lockedUntil =>
                    $"账号已锁定，请于 {lockedUntil:HH:mm:ss} 后重试",
                _ => "用户名或密码错误"
            };
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
