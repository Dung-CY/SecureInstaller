using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Errors;
using Installer.Core.Model;
using Installer.Core.Platform;
using Installer.Core.Vault;

namespace Installer.App.ViewModels;

/// <summary>Add/edit form for one package; produces a <see cref="PackageDraft"/> so admins never hand-write manifest.json.</summary>
public partial class PackageEditorViewModel : ObservableObject
{
    static readonly Regex SafeId = new(@"^[A-Za-z0-9._-]+\z");
    static readonly Regex SlugBad = new(@"[^a-z0-9._-]+");
    static readonly Regex SafeVersion = new(@"^[A-Za-z0-9._-]+\z");
    static readonly Regex LeadingVersion = new(@"^\d+(\.\d+)*");
    private readonly List<PostAction> _postActions = new();
    private readonly List<string> _existingDocs = new();

    private readonly Func<string?> _pickFile;
    private readonly Func<IReadOnlyList<string>> _pickFiles;
    private readonly Func<PackageDraft, Task> _save;
    private readonly Action _onCancel;
    private readonly Func<string, (string? Name, string? Version, string? ProductCode)> _readInfo;
    private readonly Func<string, InstallerKind> _detectKind;
    private bool _pickedUnknown;
    // Not editable: the MSI code is filled in on pick, and an existing uninstall spec is carried over untouched.
    private string? _productCode;
    private readonly UninstallSpec? _uninstall;
    static readonly Regex TrailingVersion = new(@"\s+v?\d+(\.\d+)*\z", RegexOptions.IgnoreCase);

    public PackageEditorViewModel(PackageEntry? existing, Func<string?> pickFile, Func<IReadOnlyList<string>> pickFiles,
        Func<PackageDraft, Task> save, Action onCancel,
        Func<string, (string?, string?, string?)>? readInfo = null, Func<string, InstallerKind>? detectKind = null)
    {
        _pickFile = pickFile; _pickFiles = pickFiles; _save = save; _onCancel = onCancel;
        _readInfo = readInfo ?? (p => Installer.Core.Platform.InstallerInfoReader.Read(p));
        _detectKind = detectKind ?? InstallerKindDetector.Detect;
        if (existing is null) return;

        IsEdit = true;
        _postActions = existing.PostActions?.ToList() ?? new();
        _existingDocs = existing.Docs?.ToList() ?? new();
        _keepIconPngB64 = existing.IconPngB64;
        _id = existing.Id; _name = existing.Name; _version = existing.Version; _type = existing.InstallerType; _kind = existing.Kind;
        _silentArgs = existing.SilentArgs; _targetDir = existing.TargetDir; _timeoutText = existing.TimeoutSec.ToString();
        _productCode = existing.Detect?.ProductCode; _displayNameRegex = existing.Detect?.DisplayNameRegex;
        var rules = (existing.Rules ?? new()).Select(r => r?.Type).ToHashSet();
        _blockIfWindowsCracked = rules.Contains("block_if_windows_cracked");
        _blockIfOfficeCracked = rules.Contains("block_if_office_cracked");
        _blockIfInstalledVersionGte = rules.Contains("block_if_installed_version_gte");
        _blockIfKeywordFile = rules.Contains("block_if_keyword_file");
        _requireInternet = rules.Contains("require_internet");
        _uninstall = existing.Uninstall;
    }

    public bool IsEdit { get; }
    public string Title => IsEdit ? "Sửa phần mềm" : "Thêm phần mềm";
    /// <summary>The vault only keeps encrypted copies, so files cannot be loaded back into the form.</summary>
    public string? EditHint
    {
        get
        {
            if (!IsEdit) return null;
            var sb = new StringBuilder("Kho chỉ lưu bản đã mã hóa: hãy chọn lại file cài, file đi kèm, tài liệu và script trước khi lưu.");
            if (_existingDocs.Count > 0) sb.Append(" Tài liệu hiện có: ").Append(string.Join(", ", _existingDocs)).Append('.');
            if (_postActions.Count > 0)
                sb.Append($" {_postActions.Count} thao tác sau cài được giữ; các file chúng sao chép phải được chọn lại làm file đi kèm.");
            return sb.ToString();
        }
    }

    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InstallerFileName), nameof(TypeLabel))]
    private string? _installerPath;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TypeLabel), nameof(IsMsi))]
    private InstallerType _type = InstallerType.Exe;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(KindText), nameof(KindWarning))]
    private InstallerKind _kind = InstallerKind.Unknown;
    [ObservableProperty] private string _silentArgs = "";
    [ObservableProperty] private string? _targetDir;
    [ObservableProperty] private string _timeoutText = "1800";
    [ObservableProperty] private string? _displayNameRegex;
    [ObservableProperty] private bool _keepOld;

    public string? InstallerFileName => InstallerPath is null ? null : Path.GetFileName(InstallerPath);
    public string? TypeLabel => InstallerPath is null && !IsEdit ? null : Type.ToString().ToUpperInvariant();
    public string? KindText => Kind switch
    {
        InstallerKind.Msi => "Đã nhận diện: MSI",
        InstallerKind.Inno => "Đã nhận diện: Inno Setup",
        InstallerKind.Nsis => "Đã nhận diện: NSIS",
        InstallerKind.Burn => "Đã nhận diện: WiX Burn",
        InstallerKind.InstallShield => "Đã nhận diện: InstallShield",
        InstallerKind.Zip => "Đã nhận diện: Gói zip",
        InstallerKind.Script => "Đã nhận diện: Chỉ script",
        _ => null
    };
    public string? KindWarning => Kind == InstallerKind.Unknown && _pickedUnknown
        ? "Không nhận diện được loại bộ cài — hãy nhập tham số cài ngầm" : null;
    public bool IsMsi => Type == InstallerType.Msi;

    public ObservableCollection<string> ExtraFiles { get; } = new();
    public ObservableCollection<string> DocFiles { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IconText))] private string? _iconPath;
    /// <summary>The current icon of the package being edited; written back unless the admin picks or drops it.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IconText))] private string? _keepIconPngB64;
    public string IconText => IconPath is not null ? Path.GetFileName(IconPath)
        : KeepIconPngB64 is not null ? "Giữ icon hiện tại" : "Mặc định";
    [ObservableProperty] private string? _preScriptPath;
    [ObservableProperty] private string? _postScriptPath;
    [ObservableProperty] private string _scriptTimeoutText = "300";

    [ObservableProperty] private bool _blockIfWindowsCracked;
    [ObservableProperty] private bool _blockIfOfficeCracked;
    [ObservableProperty] private bool _blockIfInstalledVersionGte;
    [ObservableProperty] private bool _blockIfKeywordFile;
    [ObservableProperty] private bool _requireInternet;

    [ObservableProperty] private string? _error;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(BusyMessage), nameof(IsIdle))]
    private bool _isBusy;
    public bool IsIdle => !IsBusy;
    public string? BusyMessage => IsBusy ? AdminViewModel.BusyText : null;

    /// <summary>"Công cụ Đồ họa" → "cong-cu-do-hoa": strip Vietnamese marks, then keep only id-safe characters.</summary>
    internal static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c is 'đ' or 'Đ' ? 'd' : c);
        return SlugBad.Replace(sb.ToString().ToLowerInvariant(), "-").Trim('-');
    }

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
        var previousDefault = InstallerKindDetector.SilentArgsFor(Kind);
        var kind = _detectKind(path);
        _pickedUnknown = kind == InstallerKind.Unknown;
        Kind = kind;
        OnPropertyChanged(nameof(KindWarning));
        if (string.IsNullOrWhiteSpace(SilentArgs) || SilentArgs == previousDefault)
            SilentArgs = InstallerKindDetector.SilentArgsFor(kind);
        var (name, version, code) = _readInfo(path);
        if (string.IsNullOrWhiteSpace(Name) && name is not null) Name = name;
        // File versions like "10.0.1 (build 5)" would fail the vault's version rule, so only the numeric part is used.
        if (string.IsNullOrWhiteSpace(Version) && version is not null) Version = LeadingVersion.Match(version).Value;
        if (string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name)) Id = Slug(Name);
        if (Type == InstallerType.Msi && string.IsNullOrWhiteSpace(_productCode) && code is not null) _productCode = code;
        if (Type == InstallerType.Exe && string.IsNullOrWhiteSpace(DisplayNameRegex) && name is not null
            && TrailingVersion.Replace(name.Trim(), "") is { Length: > 0 } baseName)
            DisplayNameRegex = "^" + Regex.Escape(baseName) + "(?![A-Za-z])";
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
    [RelayCommand] private void ClearIcon() { IconPath = null; KeepIconPngB64 = null; }
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
        if (!SafeId.IsMatch(Id?.Trim() ?? "")) return "Mã phần mềm chỉ được chứa chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới";
        if (string.IsNullOrWhiteSpace(Version)) return "Nhập phiên bản";
        if (!SafeVersion.IsMatch(Version.Trim())) return "Phiên bản chỉ được chứa chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới";
        if (string.IsNullOrEmpty(InstallerPath)) return "Chọn file cài";
        if (!TryParseSeconds(TimeoutText, out var timeout)) return "Thời gian chờ phải từ 1 đến 86400 giây";
        var scriptTimeout = 0;
        if ((PreScriptPath ?? PostScriptPath) is not null && !TryParseSeconds(ScriptTimeoutText, out scriptTimeout))
            return "Thời gian chờ của script phải từ 1 đến 86400 giây";

        var rules = new (bool On, string Type)[]
        {
            (BlockIfWindowsCracked, "block_if_windows_cracked"), (BlockIfOfficeCracked, "block_if_office_cracked"),
            (BlockIfInstalledVersionGte, "block_if_installed_version_gte"), (BlockIfKeywordFile, "block_if_keyword_file"),
            (RequireInternet, "require_internet"),
        }.Where(r => r.On).Select(r => new RuleSpec(r.Type)).ToList();
        draft = new PackageDraft(Id!.Trim(), Name.Trim(), Version.Trim(), Type, InstallerPath, SilentArgs?.Trim() ?? "",
            Opt(TargetDir), timeout, new DetectRule(Opt(_productCode), Opt(DisplayNameRegex)), ExtraFiles.ToList(),
            DocFiles.ToList(), IconPath, PreScriptPath, PostScriptPath, scriptTimeout, rules, _uninstall,
            _postActions.ToList(), IconPath is null ? KeepIconPngB64 : null, Kind);
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
