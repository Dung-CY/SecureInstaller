using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Logging;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.App.ViewModels;

public sealed record AdminPackageRow(string Name, string Version, string Id, string InstallerType, int DocCount);

public sealed record AdminUserRow(string Username, string RoleText);

public partial class AdminViewModel : ObservableObject
{
    public const string ReadOnlyMessage = "Kho đang ở chế độ chỉ đọc";
    public const string CommitPendingMessage = "Thay đổi đã ghi một phần; hãy mở lại ứng dụng để hoàn tất.";
    static readonly string[] SignatureKinds = { "kms_host", "service", "file", "hosts_block" };

    private readonly VaultSession _session;
    private readonly string _logsDir;
    private readonly string _fallbackDir;
    private readonly Func<string?> _pickFolder;
    private readonly Func<string?> _pickSavePath;
    private readonly Func<string, bool> _confirm;
    private readonly Action _onBack;
    private VaultAdmin? _admin;

    public AdminViewModel(VaultSession session, string logsDir, string fallbackDir, Func<string?> pickFolder,
        Func<string?> pickSavePath, Func<string, bool> confirm, Action onBack)
    {
        _session = session; _logsDir = logsDir; _fallbackDir = fallbackDir;
        _pickFolder = pickFolder; _pickSavePath = pickSavePath; _confirm = confirm; _onBack = onBack;
        IsReadOnly = session.Store.IsReadOnly;
        VaultFingerprint = $"Mã kho: {VaultStore.Fingerprint(session.Store.Meta.SigningPublicKeyB64)}";

        RefreshPackages();
        RefreshUsers();
        LoadSettingsFromCatalog();
        LoadLogs();
    }

    public bool IsReadOnly { get; }
    public string ReadOnlyBanner => ReadOnlyMessage;
    public string VaultFingerprint { get; }

    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _status;

    private bool CanMutate() => !IsReadOnly;

    private VaultAdmin Admin => _admin ??= new VaultAdmin(_session);

    /// <summary>Single funnel for every VaultAdmin call: maps exceptions to Error, success to Status.</summary>
    private bool Run(Action action, string? okStatus = null)
    {
        Error = null; Status = null;
        try
        {
            action();
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
            Error = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private void Back() => _onBack();

    // ---------- Packages ----------
    public List<AdminPackageRow> Packages { get; private set; } = new();
    [ObservableProperty] private AdminPackageRow? _selectedPackage;
    [ObservableProperty] private bool _keepOld;

    private void RefreshPackages()
    {
        Packages = _session.Catalog.Packages
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Version, StringComparer.Ordinal)
            .Select(p => new AdminPackageRow(p.Name, p.Version, p.Id, p.InstallerType.ToString().ToLowerInvariant(), p.Docs.Count))
            .ToList();
        SelectedPackage = null;
        OnPropertyChanged(nameof(Packages));
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void AddPackage()
    {
        var folder = _pickFolder();
        if (string.IsNullOrEmpty(folder)) return;
        PackageEntry? added = null;
        if (Run(() => added = Admin.AddPackage(folder)))
            Status = $"Đã thêm gói {added!.Name} {added.Version}";
        RefreshPackages();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void UpdatePackage()
    {
        if (SelectedPackage is not { } sel) return;
        var folder = _pickFolder();
        if (string.IsNullOrEmpty(folder)) return;
        PackageEntry? updated = null;
        if (Run(() => updated = Admin.UpdatePackage(sel.Id, folder, KeepOld)))
            Status = $"Đã cập nhật gói {updated!.Name} {updated.Version}";
        RefreshPackages();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void RemovePackage()
    {
        if (SelectedPackage is not { } sel) return;
        if (!_confirm($"Xóa gói {sel.Name} {sel.Version}?")) return;
        Run(() => Admin.RemovePackage(sel.Id, sel.Version), $"Đã xóa gói {sel.Name} {sel.Version}");
        RefreshPackages();
    }

    // ---------- Users ----------
    public List<AdminUserRow> Users { get; private set; } = new();
    [ObservableProperty] private AdminUserRow? _selectedUser;
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private UserRole _newRole = UserRole.User;

    public IReadOnlyList<UserRole> Roles { get; } = new[] { UserRole.User, UserRole.Admin };

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
    private void AddUser()
    {
        var name = NewUsername.Trim();
        if (Run(() => Admin.AddUser(name, NewPassword, NewRole), $"Đã thêm người dùng {name}"))
            NewUsername = "";
        NewPassword = "";
        RefreshUsers();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void RemoveUser()
    {
        if (SelectedUser is not { } sel) return;
        if (!_confirm($"Xóa người dùng {sel.Username}?")) return;
        Run(() => Admin.RemoveUser(sel.Username), $"Đã xóa người dùng {sel.Username}");
        RefreshUsers();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void ResetPassword()
    {
        if (SelectedUser is not { } sel) return;
        if (string.IsNullOrEmpty(NewPassword)) { Error = "Mật khẩu mới không được để trống"; return; }
        Run(() => Admin.SetPassword(sel.Username, NewPassword), $"Đã đặt lại mật khẩu cho {sel.Username}");
        NewPassword = "";
    }

    // ---------- Settings ----------
    [ObservableProperty] private string _keywordsText = "";
    [ObservableProperty] private string _pathsText = "";
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
        PathsText = string.Join("\n", c.KeywordScan.Paths);
        MaxSecondsText = c.KeywordScan.MaxSeconds.ToString();
        SignaturesText = string.Join("\n", c.CrackSignatures.Select(s => $"{s.Kind}|{s.Pattern}"));
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void SaveSettings()
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
            if (!SignatureKinds.Contains(kind) || pattern.Length == 0 || !IsValidRegex(pattern))
            {
                Error = $"Dòng {i + 1} không hợp lệ";
                return;
            }
            sigs.Add(new CrackSignature(kind, pattern));
        }
        var kw = new KeywordScanConfig(NonEmptyLines(KeywordsText), NonEmptyLines(PathsText), max);
        Run(() => Admin.UpdateSettings(kw, sigs), "Đã lưu cấu hình quét");
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

    partial void OnFromDateChanged(DateTime? value) => ApplyLogFilter();
    partial void OnToDateChanged(DateTime? value) => ApplyLogFilter();
    partial void OnUserFilterChanged(string value) => ApplyLogFilter();
    partial void OnMachineFilterChanged(string value) => ApplyLogFilter();

    [RelayCommand]
    private void LoadLogs()
    {
        try
        {
            var id = _session.Store.Meta.VaultId;
            _allLogs = EncryptedLog.ReadAll(_logsDir, id, _session.VaultKey)
                .Concat(EncryptedLog.ReadAll(_fallbackDir, id, _session.VaultKey))
                .OrderByDescending(e => e.Ts).ToList();
        }
        catch (Exception ex) { Error = ex.Message; _allLogs = new(); }
        ApplyLogFilter();
    }

    private void ApplyLogFilter()
    {
        var u = UserFilter?.Trim() ?? "";
        var m = MachineFilter?.Trim() ?? "";
        FilteredLogs = _allLogs.Where(e =>
        {
            var d = e.Ts.ToLocalTime().Date;
            if (FromDate is { } f && d < f.Date) return false;
            if (ToDate is { } t && d > t.Date) return false;
            if (u.Length > 0 && !(e.Username ?? "").Contains(u, StringComparison.OrdinalIgnoreCase)) return false;
            if (m.Length > 0 && !(e.Hostname ?? "").Contains(m, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }).ToList();
        OnPropertyChanged(nameof(FilteredLogs));
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
        catch (Exception ex) { Error = ex.Message; }
    }
}
