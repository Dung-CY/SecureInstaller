using CommunityToolkit.Mvvm.ComponentModel;

namespace Installer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _current;

    /// <summary>True while an install screen is showing and the install has not finished.</summary>
    public bool IsBusyInstalling => Current is InstallViewModel { IsDone: false };

    public void NavigateTo(object vm) => Current = vm;
}
