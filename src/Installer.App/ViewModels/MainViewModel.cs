using CommunityToolkit.Mvvm.ComponentModel;

namespace Installer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _current;

    /// <summary>True while an install screen is showing and the install has not finished.</summary>
    public bool IsBusyInstalling => Current is InstallViewModel { IsDone: false };

    /// <summary>Message to show when the user tries to close the window mid-operation; null when closing is safe.</summary>
    public string? CloseBlockedMessage => Current switch
    {
        InstallViewModel { IsDone: false } => "Đang cài đặt. Vui lòng đợi hoặc bấm Hủy trước khi thoát.",
        AdminViewModel { IsBusy: true } => "Đang xử lý. Vui lòng đợi trước khi thoát.",
        _ => null
    };

    public void NavigateTo(object vm) => Current = vm;
}
