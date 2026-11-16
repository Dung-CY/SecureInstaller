using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.App.Converters;
using Installer.Core.Errors;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Scanning;
using Installer.Core.Vault;
using Installer.Core.Versioning;

namespace Installer.App.ViewModels;

/// <summary>Visible: shown on the users' install list (unticking hides it there, it stays in the vault).</summary>
public sealed record AdminPackageRow(string Name, string Version, string Id, string InstallerType, int DocCount, bool Visible = true);

/// <summary>One software item in the bundle picker: ticked rows form the bundle, in the order they appear.</summary>
public sealed partial class BundlePickRow : ObservableObject
{
    public const string Latest = "Bản mới nhất";
    public const string MissingSuffix = " (không còn trong kho)";

    public BundlePickRow(string packageId, string name, IEnumerable<string> versions)
    {
        PackageId = packageId; Name = name;
        Versions = new[] { Latest }.Concat(versions).ToList();
        _selectedVersion = Latest;
    }

    public string PackageId { get; }
    public string Name { get; }
    public List<string> Versions { get; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _selectedVersion;
    /// <summary>1-based install position when ticked, else 0.</summary>
    [ObservableProperty] private int _order;
    internal Action<BundlePickRow>? SelectionChanged { get; set; }
    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this);

    internal BundleItem ToItem() => new(PackageId, SelectedVersion == Latest ? null
        : SelectedVersion.EndsWith(MissingSuffix, StringComparison.Ordinal) ? SelectedVersion[..^MissingSuffix.Length] : SelectedVersion);
}

public sealed record RoleOption(UserRole Role, string Label);

public sealed record AdminUserRow(string Username, string RoleText);

public sealed record AdminBundleRow(Bundle Bundle)
{
    public string Id => Bundle.Id;
    public string Name => Bundle.Name;
    public string Description => Bundle.Description ?? "";
    public int ItemCount => Bundle.Items?.Count ?? 0;
    public string ItemsText => string.Join(", ", (Bundle.Items ?? new()).Select(AdminViewModel.FormatItem));
}

/// <summary>One scan in the admin's history list; null-safe for reports written by older or damaged logs.</summary>
public sealed record ScanHistoryRow(ScanHistoryItem Item)
{
    public string TimeText => Item.Ts.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    public string Hostname => Item.Hostname;
    public string Username => Item.Username;
    public string BiosSerial => string.IsNullOrEmpty(Item.BiosSerial) ? "—" : Item.BiosSerial;
    public string Unit => Item.Report.Unit ?? "—";
    public string Manufacturer => Item.Report.Hardware.Manufacturer ?? "—";
    public string Macs => string.Join("; ", ScanHistory.PhysicalMacs(Item.Report.Hardware));
    public string DiskSerials => string.Join("; ", ScanHistory.DiskSerials(Item.Report.Hardware));
    public string Line => ScanHistory.Line(Item);
    public string Summary => $"Người dùng: {Username}\nWindows: {WindowsText}\nOffice: {OfficeText}";
    public string WindowsText => Item.Report.Windows is { } w ? DisplayText.Of(w.Verdict) : "—";
    public string OfficeText => Item.Report.Office is { } o ? DisplayText.Of(o.Verdict) : "—";
    public bool WindowsWarn => Item.Report.Windows?.Verdict is Installer.Core.Licensing.LicenseVerdict.CrackSuspected;
    public bool OfficeWarn => Item.Report.Office?.Verdict is Installer.Core.Licensing.LicenseVerdict.CrackSuspected;
}

public partial class AdminViewModel : ObservableObject
{
    public const string ReadOnlyMessage = "Kho đang ở chế độ chỉ đọc";
    public const string CommitPendingMessage = ErrorText.CommitPending;
    static readonly string[] SignatureKinds = { "kms_host", "service", "file", "hosts_block" };

    private readonly VaultSession _session;
    private readonly string _logsDir;
    private readonly string _fallbackDir;
    private readonly Func<string?> _pickFolder;
    private readonly Func<string?> _pickSavePath;
    /// <summary>Save dialog with a suggested file name; the extension picks the file type.</summary>
    private readonly Func<string, string?> _pickSaveAs;
    private readonly Func<string, bool> _confirm;
    private readonly Action _onBack;
    private VaultAdmin? _admin;
    private readonly Func<string, byte[]?>? _iconFromExe;
    private readonly Action<object>? _navigate;
    private readonly Func<string?>? _pickFile;
    private readonly Func<IReadOnlyList<string>>? _pickFiles;
    private readonly Func<Installer.Core.Install.TempWorkspace> _workspaceFactory;

    /// <summary>Tab order in AdminView: Phần mềm, Bộ cài đặt, Người dùng, Cấu hình quét, Log, Lịch sử quét.</summary>
    public const int HistoryTabIndex = 5;
    public const int HistoryPageSize = 200;

    public AdminViewModel(VaultSession session, string logsDir, string fallbackDir, Func<string?> pickFolder,
        Func<string?> pickSavePath, Func<string, bool> confirm, Action onBack,
        Func<string, byte[]?>? iconFromExe = null, Action<object>? navigate = null,
        Func<string?>? pickFile = null, Func<IReadOnlyList<string>>? pickFiles = null,
        Func<Installer.Core.Install.TempWorkspace>? workspaceFactory = null, Func<string, string?>? pickSaveAs = null)
    {
        _pickSaveAs = pickSaveAs ?? (_ => pickSavePath());
        _navigate = navigate; _pickFile = pickFile; _pickFiles = pickFiles;
        _workspaceFactory = workspaceFactory ?? (() => Installer.Core.Install.TempWorkspace.Create());
        if (!session.IsAdmin) throw new NotAdminException("Chỉ quản trị viên mới được vào khu vực này.");
        _session = session; _logsDir = logsDir; _fallbackDir = fallbackDir;
        _pickFolder = pickFolder; _pickSavePath = pickSavePath; _confirm = confirm; _onBack = onBack; _iconFromExe = iconFromExe;
        IsReadOnly = session.Store.IsReadOnly;
        VaultFingerprint = $"Mã kho: {VaultStore.Fingerprint(session.Store.Meta.SigningPublicKeyB64)}";

        RefreshPackages();
        RefreshBundles();
        RefreshUsers();
        LoadSettingsFromCatalog();
        InitialLoad = LoadLogsAsync();
    }

    /// <summary>The first log/history load started by the constructor (runs off the UI thread; never faults).</summary>
    internal Task InitialLoad { get; }

    /// <summary>Selected tab; kept on the instance so returning from a history report lands on the same tab.</summary>
    [ObservableProperty] private int _selectedTabIndex;

    public bool IsReadOnly { get; }
    public string ReadOnlyBanner => ReadOnlyMessage;
    public string VaultFingerprint { get; }

    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _status;

    public const string BusyText = "Đang xử lý…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(AddPackageCommand), nameof(UpdatePackageCommand),
        nameof(NewPackageCommand), nameof(EditPackageCommand),
        nameof(RemovePackageCommand), nameof(AddUserCommand), nameof(RemoveUserCommand),
        nameof(ResetPasswordCommand), nameof(SaveSettingsCommand), nameof(SaveBundleCommand), nameof(RemoveBundleCommand),
        nameof(OpenScanCommand), nameof(ToggleVisibleCommand))]
    [NotifyPropertyChangedFor(nameof(BusyMessage))]
    private bool _isBusy;

    public string? BusyMessage => IsBusy ? BusyText : null;

    private bool CanMutate() => !IsReadOnly && !IsBusy;
    private bool CanMutatePackage() => CanMutate() && SelectedPackage != null;
    private bool CanMutateUser() => CanMutate() && SelectedUser != null;
    private bool CanMutateBundle() => CanMutate() && SelectedBundle != null;

    private VaultAdmin Admin => _admin ??= new VaultAdmin(_session, _iconFromExe);

    /// <summary>Single funnel for every VaultAdmin call: maps exceptions to Error, success to Status.</summary>
    private async Task<bool> Run(Action action, string? okStatus = null)
    {
        Error = null; Status = null;
        IsBusy = true;
        try
        {
            await Task.Run(action);
            Status = okStatus;
            return true;
        }
        catch (CatalogCommitPendingException)
        {
            Error = CommitPendingMessage;
            return false;
        }
        catch (Exception ex)
        {
            Error = ErrorText.ForUser(ex);
            return false;
        }
        finally { IsBusy = false; }
    }

    private bool CanBack() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => _onBack();

    // ---------- Packages ----------
    public List<AdminPackageRow> Packages { get; private set; } = new();
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpdatePackageCommand), nameof(RemovePackageCommand), nameof(EditPackageCommand))]
    private AdminPackageRow? _selectedPackage;
    [ObservableProperty] private bool _keepOld;

    private void RefreshPackages()
    {
        Packages = _session.Catalog.Packages
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Version, StringComparer.Ordinal)
            .Select(p => new AdminPackageRow(p.Name, p.Version, p.Id, p.InstallerType.ToString().ToLowerInvariant(), p.Docs.Count, !p.Hidden))
            .ToList();
        SelectedPackage = null;
        OnPropertyChanged(nameof(Packages));
        LoadPick(PickedItems());
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task AddPackage()
    {
        var folder = _pickFolder();
        if (string.IsNullOrEmpty(folder)) return;
        PackageEntry? added = null;
        if (await Run(() => added = Admin.AddPackage(folder)))
            Status = $"Đã thêm phần mềm {added!.Name} {added.Version}";
        RefreshPackages();
    }

    [RelayCommand(CanExecute = nameof(CanMutatePackage))]
    private async Task UpdatePackage()
    {
        if (SelectedPackage is not { } sel) return;
        var folder = _pickFolder();
        if (string.IsNullOrEmpty(folder)) return;
        PackageEntry? updated = null;
        if (await Run(() => updated = Admin.UpdatePackage(sel.Id, folder, KeepOld)))
            Status = $"Đã cập nhật phần mềm {updated!.Name} {updated.Version}";
        RefreshPackages();
    }

    [RelayCommand(CanExecute = nameof(CanMutatePackage))]
    private async Task RemovePackage()
    {
        if (SelectedPackage is not { } sel) return;
        if (!_confirm($"Xóa phần mềm {sel.Name} {sel.Version}?")) return;
        await Run(() => Admin.RemovePackage(sel.Id, sel.Version), $"Đã xóa phần mềm {sel.Name} {sel.Version}");
        RefreshPackages();
    }

    /// <summary>The "Hiện" box: saves at once; the row is rebuilt from the catalog so a failed save shows the old state.</summary>
    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task ToggleVisible(AdminPackageRow? row)
    {
        if (row is null) return;
        var e = _session.Catalog.Packages.FirstOrDefault(p => p.Id == row.Id && p.Version == row.Version);
        if (e is null) return;
        var hide = !e.Hidden;
        await Run(() => Admin.UpdatePackageInfo(e.Id, e.Version, e with { Hidden = hide }),
            hide ? $"Đã ẩn {e.Name} {e.Version} khỏi danh sách cài" : $"Đã hiện {e.Name} {e.Version} trong danh sách cài");
        RefreshPackages();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void NewPackage() => OpenEditor(null);

    [RelayCommand(CanExecute = nameof(CanMutatePackage))]
    private void EditPackage()
    {
        if (SelectedPackage is not { } sel) return;
        OpenEditor(_session.Catalog.Packages.FirstOrDefault(p => p.Id == sel.Id && p.Version == sel.Version));
    }

    /// <summary>Navigates to the form; Cancel or a successful save returns to this instance on the Phần mềm tab.</summary>
    private void OpenEditor(PackageEntry? existing)
    {
        if (_navigate is null || _pickFile is null || _pickFiles is null) return;
        Error = null; Status = null;
        SelectedTabIndex = 0;
        PackageEditorViewModel? editor = null;
        editor = new PackageEditorViewModel(existing, _pickFile, _pickFiles,
            draft => SaveDraft(draft, existing?.Id, editor!.KeepOld), () => _navigate(this), pickFolder: _pickFolder);
        _navigate(editor);
    }

    /// <summary>Builds a temporary source folder from the form and feeds it to the same VaultAdmin calls as the folder flow.
    /// Throws on failure so the editor stays open and shows the error.</summary>
    private async Task SaveDraft(PackageDraft draft, string? updateId, bool keepOld)
    {
        PackageEntry entry;
        try
        {
            entry = await Task.Run(async () =>
            {
                if (draft.Edit is { } edit)
                    return edit.FilesChanged ? await Admin.UpdatePackageFilesAsync(draft, keepOld, _workspaceFactory)
                        : Admin.UpdatePackageInfo(edit.Existing.Id, edit.Existing.Version, PackageSource.ApplyMeta(edit.Existing, draft));
                // Plain installer copies live only in the ACL-protected workspace, never in %TEMP%.
                using var ws = _workspaceFactory();
                var dir = PackageSource.Create(draft, Path.Combine(ws.Path, "src"));
                return updateId is null ? Admin.AddPackage(dir) : Admin.UpdatePackage(updateId, dir, keepOld);
            });
        }
        finally { RefreshPackages(); }
        Error = null;
        Status = updateId is null ? $"Đã thêm phần mềm {entry.Name} {entry.Version}" : $"Đã cập nhật phần mềm {entry.Name} {entry.Version}";
        _navigate!(this);
    }

    // ---------- Bundles ----------
    public List<AdminBundleRow> Bundles { get; private set; } = new();
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RemoveBundleCommand))]
    private AdminBundleRow? _selectedBundle;
    [ObservableProperty] private string _editId = "";
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editDescription = "";
    public ObservableCollection<BundlePickRow> PickRows { get; } = new();
    [ObservableProperty] private BundlePickRow? _selectedPickRow;

    internal static string FormatItem(BundleItem i) => i.Version is null ? i.PackageId : $"{i.PackageId}@{i.Version}";

    private void RefreshBundles()
    {
        Bundles = (_session.Catalog.Bundles ?? new())
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => new AdminBundleRow(b)).ToList();
        SelectedBundle = null;
        OnPropertyChanged(nameof(Bundles));
    }

    partial void OnSelectedBundleChanged(AdminBundleRow? value)
    {
        if (value is null) return; // deselecting keeps whatever is in the editor
        EditId = value.Id;
        EditName = value.Name;
        EditDescription = value.Description;
        LoadPick(value.Bundle.Items);
    }

    [RelayCommand]
    private void NewBundle()
    {
        SelectedBundle = null;
        EditId = EditName = EditDescription = "";
        LoadPick(null);
        Error = null; Status = null;
    }

    private List<BundleItem> PickedItems() => PickRows.Where(r => r.IsSelected).Select(r => r.ToItem()).ToList();

    /// <summary>Rebuilds one row per package id in the vault, ticking and pinning the given items (selected rows first, in order).</summary>
    private void LoadPick(IReadOnlyList<BundleItem>? items)
    {
        items ??= new List<BundleItem>();
        PickRows.Clear();
        var byId = _session.Catalog.Packages.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.First().Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var rows = byId.Select(g => new BundlePickRow(g.Key, g.First().Name,
            g.Select(p => p.Version).Distinct().OrderByDescending(v => v, Comparer<string>.Create(
                (x, y) => VersionComparer.Compare(x, y) ?? string.CompareOrdinal(x, y))))).ToList();
        var picked = new List<BundlePickRow>();
        foreach (var it in items)
        {
            var row = rows.FirstOrDefault(r => string.Equals(r.PackageId, it.PackageId, StringComparison.OrdinalIgnoreCase));
            if (row is null) { row = new BundlePickRow(it.PackageId, it.PackageId, Array.Empty<string>()); } // not in vault: keep so validation reports it
            else rows.Remove(row);
            if (it.Version is { } v)
            {
                if (!row.Versions.Contains(v)) { v += BundlePickRow.MissingSuffix; row.Versions.Add(v); }
                row.SelectedVersion = v;
            }
            row.IsSelected = true;
            picked.Add(row);
        }
        foreach (var r in picked.Concat(rows)) { r.SelectionChanged = OnPickToggled; PickRows.Add(r); }
        Renumber();
    }

    private void Renumber() { var n = 0; foreach (var r in PickRows) r.Order = r.IsSelected ? ++n : 0; }

    /// <summary>A newly ticked row joins the end of the ticked block; an unticked one drops below it.</summary>
    private void OnPickToggled(BundlePickRow row)
    {
        PickRows.Remove(row);
        PickRows.Insert(PickRows.Count(r => r.IsSelected), row);
        Renumber();
    }

    private bool CanMovePick(int d) => CanMutate() && SelectedPickRow is { IsSelected: true } r
        && PickRows.IndexOf(r) + d is var i && i >= 0 && i < PickRows.Count && PickRows[i].IsSelected;
    private void MovePick(int d)
    {
        var r = SelectedPickRow!; var i = PickRows.IndexOf(r);
        PickRows.Move(i, i + d); Renumber();
        SelectedPickRow = r;
        MovePickUpCommand.NotifyCanExecuteChanged(); MovePickDownCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanMovePickUp))] private void MovePickUp() => MovePick(-1);
    [RelayCommand(CanExecute = nameof(CanMovePickDown))] private void MovePickDown() => MovePick(1);
    private bool CanMovePickUp() => CanMovePick(-1);
    private bool CanMovePickDown() => CanMovePick(1);
    partial void OnSelectedPickRowChanged(BundlePickRow? value)
    {
        MovePickUpCommand.NotifyCanExecuteChanged(); MovePickDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task SaveBundle()
    {
        Error = null; Status = null;
        var items = PickedItems();
        var bundle = new Bundle((EditId ?? "").Trim(), (EditName ?? "").Trim(), (EditDescription ?? "").Trim(), items);
        var ok = await Run(() =>
        {
            if (Admin.HasBundle(bundle.Id)) Admin.UpdateBundle(bundle);
            else Admin.AddBundle(bundle);
        }, $"Đã lưu bộ cài đặt {bundle.Name}");
        if (!ok) return;
        RefreshBundles();
        // Keep the saved bundle selected so further edits update it.
        var saved = Bundles.FirstOrDefault(b => string.Equals(b.Id, bundle.Id, StringComparison.OrdinalIgnoreCase));
        if (saved != null) SelectedBundle = saved;
    }

    [RelayCommand(CanExecute = nameof(CanMutateBundle))]
    private async Task RemoveBundle()
    {
        if (SelectedBundle is not { } sel) return;
        if (!_confirm($"Xóa bộ cài đặt {sel.Name}?")) return;
        if (await Run(() => Admin.RemoveBundle(sel.Id), $"Đã xóa bộ cài đặt {sel.Name}"))
        {
            RefreshBundles();
            EditId = EditName = EditDescription = "";
            LoadPick(null);
        }
    }

    // ---------- Users ----------
    public List<AdminUserRow> Users { get; private set; } = new();
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RemoveUserCommand), nameof(ResetPasswordCommand))]
    private AdminUserRow? _selectedUser;
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private UserRole _newRole = UserRole.User;

    public IReadOnlyList<RoleOption> Roles { get; } =
        new[] { new RoleOption(UserRole.User, "Người dùng"), new RoleOption(UserRole.Admin, "Quản trị") };

    private void RefreshUsers()
    {
        // Admin status = holds a wrapped signing key; the Role field is never trusted.
        Users = _session.Store.Meta.Users
            .Select(u => new AdminUserRow(u.Username, u.WrappedSigningKeyB64 != null ? "Quản trị" : "Người dùng"))
            .ToList();
        SelectedUser = null;
        OnPropertyChanged(nameof(Users));
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task AddUser()
    {
        var name = NewUsername.Trim();
        if (await Run(() => Admin.AddUser(name, NewPassword, NewRole), $"Đã thêm người dùng {name}"))
            NewUsername = "";
        NewPassword = "";
        RefreshUsers();
    }

    [RelayCommand(CanExecute = nameof(CanMutateUser))]
    private async Task RemoveUser()
    {
        if (SelectedUser is not { } sel) return;
        if (!_confirm($"Xóa người dùng {sel.Username}?")) return;
        await Run(() => Admin.RemoveUser(sel.Username), $"Đã xóa người dùng {sel.Username}");
        RefreshUsers();
    }

    [RelayCommand(CanExecute = nameof(CanMutateUser))]
    private async Task ResetPassword()
    {
        if (SelectedUser is not { } sel) return;
        if (string.IsNullOrEmpty(NewPassword)) { Error = "Mật khẩu mới không được để trống"; return; }
        await Run(() => Admin.SetPassword(sel.Username, NewPassword), $"Đã đặt lại mật khẩu cho {sel.Username}");
        NewPassword = "";
    }

    // ---------- Settings ----------
    [ObservableProperty] private string _keywordsText = "";
    [ObservableProperty] private string _pathsText = "";
    [ObservableProperty] private bool _scanDesktops;
    [ObservableProperty] private bool _scanDownloads;
    [ObservableProperty] private bool _scanDataDrives;
    [ObservableProperty] private string _maxSecondsText = "60";
    [ObservableProperty] private string _signaturesText = "";

    private static string[] SplitLines(string text) =>
        text.Split('\n').Select(l => l.Trim()).ToArray();

    private static List<string> NonEmptyLines(string text) =>
        SplitLines(text).Where(l => l.Length > 0).ToList();

    private void LoadSettingsFromCatalog()
    {
        var c = _session.Catalog;
        KeywordsText = string.Join("\n", c.KeywordScan.Keywords);
        var tokens = new[] { KeywordFileScanner.DesktopToken, KeywordFileScanner.DownloadsToken, KeywordFileScanner.DataDrivesToken };
        bool IsToken(string p) => tokens.Any(t => p.Trim().Equals(t, StringComparison.OrdinalIgnoreCase));
        ScanDesktops = c.KeywordScan.Paths.Any(p => p.Trim().Equals(tokens[0], StringComparison.OrdinalIgnoreCase));
        ScanDownloads = c.KeywordScan.Paths.Any(p => p.Trim().Equals(tokens[1], StringComparison.OrdinalIgnoreCase));
        ScanDataDrives = c.KeywordScan.Paths.Any(p => p.Trim().Equals(tokens[2], StringComparison.OrdinalIgnoreCase));
        PathsText = string.Join("\n", c.KeywordScan.Paths.Where(p => !IsToken(p)));
        MaxSecondsText = c.KeywordScan.MaxSeconds.ToString();
        SignaturesText = string.Join("\n", c.CrackSignatures.Select(s => $"{s.Kind}|{s.Pattern}"));
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task SaveSettings()
    {
        Error = null; Status = null;
        if (!int.TryParse(MaxSecondsText.Trim(), out var max) || max < 1 || max > 3600)
        {
            Error = "Số giây phải từ 1 đến 3600";
            return;
        }
        var sigs = new List<CrackSignature>();
        var lines = SplitLines(SignaturesText);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var bar = lines[i].IndexOf('|');
            var kind = bar < 0 ? "" : lines[i][..bar].Trim();
            var pattern = bar < 0 ? "" : lines[i][(bar + 1)..].Trim();
            // "file" patterns are paths (e.g. %ProgramFiles%\KMSpico), not regexes.
            if (!SignatureKinds.Contains(kind) || pattern.Length == 0 || (kind != "file" && !IsValidRegex(pattern)))
            {
                Error = $"Dấu hiệu crack: dòng {i + 1} không hợp lệ";
                return;
            }
            sigs.Add(new CrackSignature(kind, pattern));
        }
        var paths = NonEmptyLines(PathsText);
        if (ScanDesktops) paths.Add(KeywordFileScanner.DesktopToken);
        if (ScanDownloads) paths.Add(KeywordFileScanner.DownloadsToken);
        if (ScanDataDrives) paths.Add(KeywordFileScanner.DataDrivesToken);
        var kw = new KeywordScanConfig(NonEmptyLines(KeywordsText), paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), max);
        await Run(() => Admin.UpdateSettings(kw, sigs), "Đã lưu cấu hình quét");
    }

    private static bool IsValidRegex(string p)
    {
        try { _ = new Regex(p); return true; }
        catch (ArgumentException) { return false; }
    }

    // ---------- Logs ----------
    private List<LogEntry> _allLogs = new();
    public List<LogEntry> FilteredLogs { get; private set; } = new();
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private string _userFilter = "";
    [ObservableProperty] private string _machineFilter = "";

    [ObservableProperty] private bool _isLoadingLogs;

    partial void OnFromDateChanged(DateTime? value) => ApplyLogFilter();
    partial void OnToDateChanged(DateTime? value) => ApplyLogFilter();
    partial void OnUserFilterChanged(string value) => ApplyLogFilter();
    partial void OnMachineFilterChanged(string value) => ApplyLogFilter();

    private int _loadSeq;

    /// <summary>
    /// Reads and decrypts the logs (vault + fallback) and rebuilds the scan history on a worker thread, so a large
    /// log folder never freezes the window. Feeds both the Log and the Lịch sử quét tab; a successful load clears Error.
    /// </summary>
    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        var seq = ++_loadSeq;
        IsLoadingLogs = true;
        var errorAtStart = Error;
        List<LogEntry> logs;
        List<ScanHistoryItem> history;
        string? error = null;
        try
        {
            var id = _session.Store.Meta.VaultId;
            var vk = _session.VaultKey;
            (logs, history) = await Task.Run(() =>
            {
                var all = EncryptedLog.ReadAll(_logsDir, id, vk)
                    .Concat(EncryptedLog.ReadAll(_fallbackDir, id, vk))
                    .OrderByDescending(e => e.Ts).ToList();
                return (all, ScanHistory.From(all));
            });
        }
        catch (Exception ex) { error = ErrorText.ForUser(ex); logs = new(); history = new(); }
        if (seq != _loadSeq) return; // a newer load superseded this one
        _allLogs = logs;
        _history = history;
        // Only touch Error if nothing else (e.g. an admin operation that ran meanwhile) changed it during the load.
        if (Error == errorAtStart) Error = error;
        IsLoadingLogs = false;
        ApplyFilters();
    }

    /// <summary>Re-applies both tabs' filters after a load; the history goes back to page 1.</summary>
    private void ApplyFilters()
    {
        ApplyLogFilter();
        ApplyHistoryFilterFromStart();
    }

    private static bool InDateRange(DateTimeOffset ts, DateTime? from, DateTime? to)
    {
        var d = ts.ToLocalTime().Date;
        if (from is { } f && d < f.Date) return false;
        if (to is { } t && d > t.Date) return false;
        return true;
    }

    private static bool Matches(string? value, string filter) =>
        filter.Length == 0 || (value ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void ApplyLogFilter()
    {
        var u = UserFilter?.Trim() ?? "";
        var m = MachineFilter?.Trim() ?? "";
        FilteredLogs = _allLogs.Where(e => InDateRange(e.Ts, FromDate, ToDate) && Matches(e.Username, u) && Matches(e.Hostname, m)).ToList();
        OnPropertyChanged(nameof(FilteredLogs));
    }

    // ---------- Scan history ----------
    private List<ScanHistoryItem> _history = new();
    private List<ScanHistoryItem> _filteredHistory = new();
    public List<ScanHistoryRow> HistoryPage { get; private set; } = new();

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(NextPageCommand), nameof(PrevPageCommand))]
    private int _historyPageIndex;

    public int HistoryCount => _filteredHistory.Count;
    public int HistoryPageCount => Math.Max(1, (_filteredHistory.Count + HistoryPageSize - 1) / HistoryPageSize);
    public string PageText => $"Trang {HistoryPageIndex + 1}/{HistoryPageCount}";

    // The history tab has its own filters, independent of the Log tab's.
    [ObservableProperty] private string _historyMachineFilter = "";
    [ObservableProperty] private DateTime? _historyFrom;
    [ObservableProperty] private DateTime? _historyTo;

    partial void OnHistoryMachineFilterChanged(string value) => ApplyHistoryFilterFromStart();
    partial void OnHistoryFromChanged(DateTime? value) => ApplyHistoryFilterFromStart();
    partial void OnHistoryToChanged(DateTime? value) => ApplyHistoryFilterFromStart();

    /// <summary>Any filter change shows page 1 of the new result.</summary>
    private void ApplyHistoryFilterFromStart()
    {
        HistoryPageIndex = 0;
        var m = HistoryMachineFilter?.Trim() ?? "";
        _filteredHistory = _history.Where(h => InDateRange(h.Ts, HistoryFrom, HistoryTo) && (Matches(h.Hostname, m) || Matches(h.Report.Unit, m))).ToList();
        ShowHistoryPage();
    }

    private void ShowHistoryPage()
    {
        if (HistoryPageIndex >= HistoryPageCount) HistoryPageIndex = HistoryPageCount - 1;
        HistoryPage = _filteredHistory.Skip(HistoryPageIndex * HistoryPageSize).Take(HistoryPageSize)
            .Select(h => new ScanHistoryRow(h)).ToList();
        OnPropertyChanged(nameof(HistoryPage));
        OnPropertyChanged(nameof(HistoryCount));
        OnPropertyChanged(nameof(HistoryPageCount));
        OnPropertyChanged(nameof(PageText));
        NextPageCommand.NotifyCanExecuteChanged();
        PrevPageCommand.NotifyCanExecuteChanged();
    }

    private bool CanNextPage() => HistoryPageIndex + 1 < HistoryPageCount;
    private bool CanPrevPage() => HistoryPageIndex > 0;

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private void NextPage()
    {
        if (!CanNextPage()) return;
        HistoryPageIndex++;
        ShowHistoryPage();
    }

    [RelayCommand(CanExecute = nameof(CanPrevPage))]
    private void PrevPage()
    {
        if (!CanPrevPage()) return;
        HistoryPageIndex--;
        ShowHistoryPage();
    }

    private bool CanOpenScan(ScanHistoryItem? item) => _navigate != null && !IsBusy;

    /// <summary>Opens a past scan read-only in the normal report view; Back returns to this instance on the history tab.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenScan))]
    private void OpenScan(ScanHistoryItem? item)
    {
        if (item is null || _navigate is null) return;
        Error = null;
        var title = string.Create(CultureInfo.InvariantCulture,
            $"Lịch sử quét — {item.Hostname} — {item.Ts.ToLocalTime():dd/MM/yyyy HH:mm}");
        ScanReportViewModel report;
        try { report = ScanReportViewModel.ForReport(item.Report, title, () => _navigate(this)); }
        catch (Exception ex) { Error = ErrorText.ForUser(ex); return; }
        SelectedTabIndex = HistoryTabIndex;
        _navigate(report);
    }

    [RelayCommand]
    private void ExportHistoryCsv()
    {
        var path = _pickSaveAs("lich-su-quet.csv");
        if (string.IsNullOrEmpty(path)) return;
        Error = null; Status = null;
        try
        {
            CsvExporter.ExportScans(_filteredHistory, path);
            Status = $"Đã xuất {_filteredHistory.Count} lần quét";
        }
        catch (Exception ex) { Error = ErrorText.ForUser(ex); }
    }

    /// <summary>The " | " template as a .txt file.</summary>
    [RelayCommand]
    private void ExportHistoryText()
    {
        var path = _pickSaveAs("lich-su-quet.txt");
        if (string.IsNullOrEmpty(path)) return;
        Error = null; Status = null;
        try
        {
            ScanHistory.ExportText(_filteredHistory, path);
            Status = $"Đã xuất {_filteredHistory.Count} lần quét theo mẫu";
        }
        catch (Exception ex) { Error = ErrorText.ForUser(ex); }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var path = _pickSavePath();
        if (string.IsNullOrEmpty(path)) return;
        Error = null; Status = null;
        try
        {
            CsvExporter.Export(FilteredLogs, path);
            Status = $"Đã xuất {FilteredLogs.Count} dòng log";
        }
        catch (Exception ex) { Error = ErrorText.ForUser(ex); }
    }
}
