using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Station.Desktop.ViewModels;

/// <summary>
/// PIN 输入对话框 ViewModel。触屏友好：数字键盘按钮 + 键盘输入双通道。
/// </summary>
public partial class PinViewModel : ObservableObject
{
    /// <summary>对话框请求关闭（true=验证通过，false=取消）</summary>
    public event Action<bool>? CloseRequested;

    private readonly string _correctPin;

    /// <summary>PIN 位数</summary>
    public int PinLength { get; }

    /// <summary>当前已输入的 PIN（明文，仅供内部对比）</summary>
    [ObservableProperty]
    private string _enteredPin = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>掩码显示：已输入位用 ●，未输入位用 ○，用空格分隔</summary>
    public string MaskDisplay => BuildMask();

    public PinViewModel(string correctPin, int pinLength)
    {
        _correctPin = correctPin ?? string.Empty;
        PinLength = Math.Clamp(pinLength, 4, 8);
    }

    // ============================================================
    // 命令（供 XAML 绑定）
    // ============================================================

    [RelayCommand]
    private void Append(string? digit)
    {
        if (string.IsNullOrEmpty(digit)) return;
        if (EnteredPin.Length >= PinLength) return;

        ClearError();
        EnteredPin += digit[0];

        // 满位自动提交
        if (EnteredPin.Length == PinLength)
        {
            TryVerify();
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        ClearError();
        if (EnteredPin.Length > 0)
        {
            EnteredPin = EnteredPin[..^1];
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        ClearError();
        EnteredPin = string.Empty;
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void Submit()
    {
        if (EnteredPin.Length == PinLength)
        {
            TryVerify();
        }
    }

    // ============================================================
    // 内部逻辑
    // ============================================================

    private void TryVerify()
    {
        if (string.Equals(EnteredPin, _correctPin, StringComparison.Ordinal))
        {
            CloseRequested?.Invoke(true);
            return;
        }

        // 验证失败：清空 + 提示
        EnteredPin = string.Empty;
        ErrorMessage = "PIN 错误，请重试";
        HasError = true;
    }

    private void ClearError()
    {
        if (!HasError) return;
        ErrorMessage = string.Empty;
        HasError = false;
    }

    private string BuildMask()
    {
        var chars = new char[PinLength];
        for (var i = 0; i < PinLength; i++)
        {
            chars[i] = i < EnteredPin.Length ? '●' : '○';
        }
        return string.Join(' ', chars);
    }

    // 输入变化时通知掩码刷新
    partial void OnEnteredPinChanged(string value)
        => OnPropertyChanged(nameof(MaskDisplay));

    partial void OnHasErrorChanged(bool value)
        => OnPropertyChanged(nameof(MaskDisplay));
}
