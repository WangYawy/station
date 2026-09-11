using CommunityToolkit.Mvvm.ComponentModel;

namespace Station.Desktop.ViewModels;

public partial class ModulePlaceholderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _message;

    public ModulePlaceholderViewModel(string title, string message)
    {
        _title = title;
        _message = message;
    }
}
