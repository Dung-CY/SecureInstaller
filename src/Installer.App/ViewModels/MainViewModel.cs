using CommunityToolkit.Mvvm.ComponentModel;

namespace Installer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _current;

    /// <summary>Message to show when the user tries to close the window mid-operation; null when closing is safe.</summary>
    public string? CloseBlockedMessage => Current switch
    {
        InstallViewModel { IsDone: false } => "Đang cài đặt. Vui lòng đợi hoặc bấm Hủy trước khi thoát.",
        AdminViewModel { IsBusy: true } => "Đang xử lý. Vui lòng đợi trước khi thoát.",
        _ => null
    };

    public void NavigateTo(object vm) => Current = vm;
}
