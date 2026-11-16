using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Installer.App.ViewModels;

/// <summary>Shown instead of the install list while Secure Boot is on and the admin requires it off. Cannot be skipped.</summary>
public sealed partial class SecureBootBlockViewModel : ObservableObject
{
    private readonly Func<bool?> _recheck;
    private readonly Action _restart;
    private readonly Action _exit;
    private readonly Action _onCleared;

    public SecureBootBlockViewModel(string message, string guide, Func<bool?> recheck, Action restart, Action exit, Action onCleared)
    {
        Message = message; Guide = guide;
        _recheck = recheck; _restart = restart; _exit = exit; _onCleared = onCleared;
    }

    public string Message { get; }
    public string Guide { get; }
    [ObservableProperty] private string? _status;

    [RelayCommand]
    private void Restart() => _restart();

    /// <summary>After the user turns Secure Boot off and reboots, the app restarts and re-checks; this is for an in-session re-check.</summary>
    [RelayCommand]
    private void Recheck()
    {
        if (_recheck() != true) _onCleared();
        else Status = "Secure Boot vẫn đang bật. Hãy tắt trong BIOS rồi khởi động lại.";
    }

    [RelayCommand]
    private void Exit() => _exit();
}
