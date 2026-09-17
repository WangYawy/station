using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Station.Application.Session;

namespace Station.Desktop.ViewModels;

/// <summary>
/// 退出确认对话框 ViewModel（纯提示，登录逻辑由 LoginDialog 统一处理）。
/// </summary>
public partial class ExitConfirmViewModel : ObservableObject
{
    /// <summary>对话框请求关闭（true=允许退出，false=取消）</summary>
    public event Action<bool>? CloseRequested;

    public string Title => "退出采集站";

    public string Message => "确定要退出采集站吗？";

    public string ConfirmText => "退出程序";

    public string CurrentUserName { get; }

    public ExitConfirmViewModel(ISessionManager sessions)
    {
        CurrentUserName = sessions.Current?.Name
                          ?? sessions.Current?.UserName
                          ?? "未登录";
    }

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
