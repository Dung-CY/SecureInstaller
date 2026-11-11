using CommunityToolkit.Mvvm.ComponentModel;

namespace Installer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _current;

    public void NavigateTo(object vm) => Current = vm;
}
