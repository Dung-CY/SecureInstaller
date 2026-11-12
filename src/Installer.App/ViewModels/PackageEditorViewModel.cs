using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Errors;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.App.ViewModels;

public sealed record UninstallOption(string? Type, string Label);

/// <summary>Add/edit form for one package; produces a <see cref="PackageDraft"/> so admins never hand-write manifest.json.</summary>
public partial class PackageEditorViewModel : ObservableObject
{
    static readonly Regex SafeId = new(@"^[A-Za-z0-9._-]+\z");
    static readonly Regex SlugBad = new(@"[^a-z0-9._-]");

    private readonly Func<string?> _pickFile;
    private readonly Func<IReadOnlyList<string>> _pickFiles;
    private readonly Func<PackageDraft, Task> _save;
    private readonly Action _onCancel;
    private readonly Func<string, (string? Name, string? Version, string? ProductCode)> _readInfo;

    public PackageEditorViewModel(PackageEntry? existing, Func<string?> pickFile, Func<IReadOnlyList<string>> pickFiles,
        Func<PackageDraft, Task> save, Action onCancel,
        Func<string, (string?, string?, string?)>? readInfo = null)
    {
        _pickFile = pickFile; _pickFiles = pickFiles; _save = save; _onCancel = onCancel;
        _readInfo = readInfo ?? (p => Installer.Core.Platform.InstallerInfoReader.Read(p));
        _selectedUninstall = UninstallOptions[0];
        if (existing is null) return;

        IsEdit = true;
        _id = existing.Id; _name = existing.Name; _version = existing.Version; _type = existing.InstallerType;
        _silentArgs = existing.SilentArgs; _targetDir = existing.TargetDir; _timeoutText = existing.TimeoutSec.ToString();
        _productCode = existing.Detect?.ProductCode; _displayNameRegex = existing.Detect?.DisplayNameRegex;
        var rules = (existing.Rules ?? new()).Select(r => r?.Type).ToHashSet();
        _blockIfWindowsCracked = rules.Contains("block_if_windows_cracked");
        _blockIfOfficeCracked = rules.Contains("block_if_office_cracked");
        _blockIfInstalledVersionGte = rules.Contains("block_if_installed_version_gte");
        _blockIfKeywordFile = rules.Contains("block_if_keyword_file");
        _requireInternet = rules.Contains("require_internet");
        if (existing.Uninstall is { } u)
        {
            _selectedUninstall = UninstallOptions.FirstOrDefault(o => o.Type == u.Type) ?? UninstallOptions[0];
            _uninstallCommand = u.Command; _uninstallArgs = u.Args;
        }
    }

    public bool IsEdit { get; }
    public string Title => IsEdit ? "Sửa phần mềm" : "Thêm phần mềm";
    /// <summary>The vault only keeps encrypted copies, so files cannot be loaded back into the form.</summary>
    public string? EditHint => IsEdit
        ? "Kho chỉ lưu bản đã mã hóa: hãy chọn lại file cài, file đi kèm, tài liệu, icon và script trước khi lưu."
        : null;

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InstallerFileName), nameof(TypeLabel))]
    private string? _installerPath;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TypeLabel), nameof(IsMsi))]
    private InstallerType _type = InstallerType.Exe;
    [ObservableProperty] private string _silentArgs = "";
    [ObservableProperty] private string? _targetDir;
    [ObservableProperty] private string _timeoutText = "1800";
    [ObservableProperty] private string? _productCode;
    [ObservableProperty] private string? _displayNameRegex;
    [ObservableProperty] private bool _keepOld;

    public string? InstallerFileName => InstallerPath is null ? null : Path.GetFileName(InstallerPath);
    public string? TypeLabel => InstallerPath is null && !IsEdit ? null : Type.ToString().ToUpperInvariant();
    public bool IsMsi => Type == InstallerType.Msi;

    public ObservableCollection<string> ExtraFiles { get; } = new();
    public ObservableCollection<string> DocFiles { get; } = new();
    [ObservableProperty] private string? _iconPath;
    [ObservableProperty] private string? _preScriptPath;
    [ObservableProperty] private string? _postScriptPath;
    [ObservableProperty] private string _scriptTimeoutText = "300";

    [ObservableProperty] private bool _blockIfWindowsCracked;
    [ObservableProperty] private bool _blockIfOfficeCracked;
    [ObservableProperty] private bool _blockIfInstalledVersionGte;
    [ObservableProperty] private bool _blockIfKeywordFile;
    [ObservableProperty] private bool _requireInternet;

    public IReadOnlyList<UninstallOption> UninstallOptions { get; } = new[]
    {
        new UninstallOption(null, "Không hỗ trợ gỡ"),
        new UninstallOption("msi", "Gỡ bằng MSI"),
        new UninstallOption("command", "Chạy lệnh gỡ"),
        new UninstallOption("zip", "Xóa thư mục đã giải nén"),
    };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUninstallCommand))]
    private UninstallOption _selectedUninstall;
    [ObservableProperty] private string? _uninstallCommand;
    [ObservableProperty] private string? _uninstallArgs;
    public bool IsUninstallCommand => SelectedUninstall.Type == "command";

    [ObservableProperty] private string? _error;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(BusyMessage))]
    private bool _isBusy;
    public string? BusyMessage => IsBusy ? AdminViewModel.BusyText : null;

    internal static string Slug(string name) => SlugBad.Replace(name.Trim().ToLowerInvariant(), "-");

    [RelayCommand]
    private void PickInstaller()
    {
        var path = _pickFile();
        if (string.IsNullOrEmpty(path)) return;
        InstallerPath = path;
        Type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".msi" => InstallerType.Msi,
            ".zip" => InstallerType.Zip,
            _ => InstallerType.Exe
        };
        var (name, version, code) = _readInfo(path);
        if (string.IsNullOrWhiteSpace(Name) && name is not null) Name = name;
        if (string.IsNullOrWhiteSpace(Version) && version is not null) Version = version;
        if (string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name)) Id = Slug(Name);
        if (Type == InstallerType.Msi && string.IsNullOrWhiteSpace(ProductCode) && code is not null) ProductCode = code;
    }

    private static void AddDistinct(ObservableCollection<string> list, IEnumerable<string> files)
    {
        foreach (var f in files)
            if (!list.Contains(f, StringComparer.OrdinalIgnoreCase)) list.Add(f);
    }

    [RelayCommand] private void AddExtraFiles() => AddDistinct(ExtraFiles, _pickFiles());
    [RelayCommand] private void RemoveExtraFile(string? f) { if (f != null) ExtraFiles.Remove(f); }
    [RelayCommand] private void AddDocs() => AddDistinct(DocFiles, _pickFiles());
    [RelayCommand] private void RemoveDoc(string? f) { if (f != null) DocFiles.Remove(f); }
    [RelayCommand] private void PickIcon() { if (_pickFile() is { Length: > 0 } p) IconPath = p; }
    [RelayCommand] private void ClearIcon() => IconPath = null;
    [RelayCommand] private void PickPreScript() { if (_pickFile() is { Length: > 0 } p) PreScriptPath = p; }
    [RelayCommand] private void ClearPreScript() => PreScriptPath = null;
    [RelayCommand] private void PickPostScript() { if (_pickFile() is { Length: > 0 } p) PostScriptPath = p; }
    [RelayCommand] private void ClearPostScript() => PostScriptPath = null;

    private static string? Opt(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool TryParseSeconds(string? text, out int sec) =>
        int.TryParse(text?.Trim(), out sec) && sec is >= 1 and <= 86400;

    /// <summary>Returns the first validation error, or builds the draft.</summary>
    private string? Validate(out PackageDraft? draft)
    {
        draft = null;
        if (string.IsNullOrWhiteSpace(Name)) return "Nhập tên phần mềm";
        if (!SafeId.IsMatch(Id?.Trim() ?? "")) return "Mã gói chỉ được chứa chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới";
        if (string.IsNullOrWhiteSpace(Version)) return "Nhập phiên bản";
        if (string.IsNullOrEmpty(InstallerPath)) return "Chọn file cài";
        if (!TryParseSeconds(TimeoutText, out var timeout)) return "Thời gian chờ phải từ 1 đến 86400 giây";
        var scriptTimeout = 0;
        if ((PreScriptPath ?? PostScriptPath) is not null && !TryParseSeconds(ScriptTimeoutText, out scriptTimeout))
            return "Thời gian chờ của script phải từ 1 đến 86400 giây";
        if (IsUninstallCommand && string.IsNullOrWhiteSpace(UninstallCommand)) return "Nhập lệnh gỡ";

        var rules = new (bool On, string Type)[]
        {
            (BlockIfWindowsCracked, "block_if_windows_cracked"), (BlockIfOfficeCracked, "block_if_office_cracked"),
            (BlockIfInstalledVersionGte, "block_if_installed_version_gte"), (BlockIfKeywordFile, "block_if_keyword_file"),
            (RequireInternet, "require_internet"),
        }.Where(r => r.On).Select(r => new RuleSpec(r.Type)).ToList();
        var uninstall = SelectedUninstall.Type is { } t
            ? new UninstallSpec(t, IsUninstallCommand ? Opt(UninstallCommand) : null, IsUninstallCommand ? Opt(UninstallArgs) : null)
            : null;
        draft = new PackageDraft(Id!.Trim(), Name.Trim(), Version.Trim(), Type, InstallerPath, SilentArgs?.Trim() ?? "",
            Opt(TargetDir), timeout, new DetectRule(Opt(ProductCode), Opt(DisplayNameRegex)), ExtraFiles.ToList(),
            DocFiles.ToList(), IconPath, PreScriptPath, PostScriptPath, scriptTimeout, rules, uninstall);
        return null;
    }

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task Save()
    {
        Error = Validate(out var draft);
        if (draft is null) return;
        IsBusy = true;
        try { await _save(draft); }
        catch (Exception ex) { Error = ErrorText.ForUser(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void Cancel() => _onCancel();
}
