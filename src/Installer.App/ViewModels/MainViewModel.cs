using CommunityToolkit.Mvvm.ComponentModel;
using Installer.Core;
using Installer.Core.Model;

namespace Installer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? _current;

    [ObservableProperty] private string? _vaultInfo;
    [ObservableProperty] private string? _readOnlyNotice;

    public void SetVault(Catalog catalog, bool readOnly)
    {
        VaultInfo = string.Format(System.Globalization.CultureInfo.InvariantCulture, Strings.VaultInfo, catalog.Version, catalog.UpdatedAt.ToLocalTime());
        ReadOnlyNotice = readOnly ? Strings.ReadOnlyNotice : null;
    }

    /// <summary>Message to show when the user tries to close the window mid-operation; null when closing is safe.</summary>
    public string? CloseBlockedMessage => Current switch
    {
        InstallViewModel { IsDone: false } => "Đang cài đặt. Vui lòng đợi hoặc bấm Hủy trước khi thoát.",
        AdminViewModel { IsBusy: true } => "Đang xử lý. Vui lòng đợi trước khi thoát.",
        _ => null
    };

    public void NavigateTo(object vm) => Current = vm;
}
