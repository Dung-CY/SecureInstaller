using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Model;

namespace Installer.App.ViewModels;

/// <summary>One item line on a bundle card; SkipReason is null when the item would be queued.</summary>
public sealed record BundleCardItem(string Name, string StatusText, string? SkipReason);

/// <summary>A card on the "Bộ cài đặt" tab: the bundle, its items with live status, and "Cài bộ này".</summary>
public sealed partial class BundleCardViewModel : ObservableObject
{
    private readonly PackageListViewModel _owner;

    internal BundleCardViewModel(Bundle bundle, PackageListViewModel owner)
    {
        Bundle = bundle; _owner = owner;
        Items = Build();
    }

    public Bundle Bundle { get; }
    public string Name => Bundle.Name;
    public string Description => Bundle.Description;
    [ObservableProperty] private IReadOnlyList<BundleCardItem> _items;

    private IReadOnlyList<BundleCardItem> Build() => BundleResolver.Resolve(_owner.Catalog, Bundle)
        .Select(x =>
        {
            var (row, skip) = _owner.Classify(x.Package);
            return new BundleCardItem(row?.Name ?? x.Item.PackageId, row?.StatusText ?? "", skip);
        }).ToList();

    /// <summary>Re-reads the rows' status (after an install or while one is running).</summary>
    internal void Refresh()
    {
        Items = Build();
        InstallCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstall() => !_owner.IsInstalling && Items.Any(i => i.SkipReason is null);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install() => _owner.InstallBundle(Bundle);
}
