using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Errors;
using Installer.Core.Platform;

namespace Installer.App.ViewModels;

/// <summary>What the user entered right after login; kept for this session only.</summary>
public sealed record SessionInfo(string Unit, string? PendingComputerName);

/// <summary>After login: the unit (required) and an optional computer rename, before any scan or install.</summary>
public sealed partial class SessionSetupViewModel : ObservableObject
{
    private readonly Action<string> _rename;
    private readonly Action<SessionInfo> _onContinue;

    public SessionSetupViewModel(string currentName, string? pendingName, bool domainJoined, string? lastUnit,
        Action<string> rename, Action<SessionInfo> onContinue)
    {
        CurrentName = currentName; PendingName = pendingName; DomainJoined = domainJoined;
        _rename = rename; _onContinue = onContinue;
        _unit = lastUnit ?? "";
    }

    public string CurrentName { get; }
    public string? PendingName { get; }
    public bool DomainJoined { get; }
    public bool CanRename => !DomainJoined;
    public string NameHint => $"Ví dụ: PTH-KT-QLVBDH01. Nên dài tối đa {ComputerName.NetBiosLength} ký tự để không bị trùng tên khi chia sẻ qua mạng LAN.";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(UnitPreview))] private string _unit;
    public string UnitPreview => ComputerName.NormalizeUnit(Unit) is { Length: > 0 } u ? "Sẽ lưu là: " + u : "";

    [ObservableProperty] private bool _wantRename;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameLengthText), nameof(NameTooLong))] private string _newName = "";
    public string NameLengthText => $"{ComputerName.Normalize(NewName).Length}/{ComputerName.NetBiosLength} ký tự";
    public bool NameTooLong => ComputerName.Normalize(NewName).Length > ComputerName.NetBiosLength;

    [ObservableProperty] private string? _error;

    [RelayCommand]
    private void Continue()
    {
        Error = null;
        var unit = ComputerName.NormalizeUnit(Unit);
        if (unit.Length == 0) { Error = "Nhập tên đơn vị"; return; }
        string? renamed = PendingName;
        if (WantRename && CanRename)
        {
            var name = ComputerName.Normalize(NewName);
            if (ComputerName.Validate(name) is { } err) { Error = err; return; }
            try { _rename(name); renamed = name; }
            catch (Exception ex) { Error = "Không đổi được tên máy: " + ErrorText.ForUser(ex); return; }
        }
        _onContinue(new SessionInfo(unit, renamed));
    }
}
