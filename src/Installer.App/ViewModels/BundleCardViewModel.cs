using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Model;

namespace Installer.App.ViewModels;

/// <summary>One item line on a bundle card; SkipReason is null when the item would be queued.</summary>
public sealed record BundleCardItem(string Name, string StatusText, string? SkipReason, string Version = "", byte[]? Icon = null);

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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SummaryText))] private IReadOnlyList<BundleCardItem> _items;

    public string SummaryText
    {
        get
        {
            var ready = Items.Count(i => i.SkipReason is null);
            var text = ready == Items.Count ? $"{Items.Count} phần mềm" : $"{Items.Count} phần mềm · {ready} sẽ được cài";
            return string.IsNullOrWhiteSpace(Description) ? text : text + " · " + Description;
        }
    }

    private IReadOnlyList<BundleCardItem> Build() => BundleResolver.Resolve(_owner.Catalog, Bundle)
        .Select(x =>
        {
            var (row, skip) = _owner.Classify(x.Package);
            return new BundleCardItem(row?.Name ?? x.Item.PackageId, row?.StatusText ?? "", skip,
                x.Package?.Version ?? "", row?.Icon);
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
