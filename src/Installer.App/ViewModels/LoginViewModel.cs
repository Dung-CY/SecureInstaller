using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Errors;
using Installer.Core;
using Installer.Core.Vault;

namespace Installer.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly Func<VaultStore> _openVault;
    private readonly Action<VaultSession> _onSuccess;
    private readonly TimeProvider _time;

    private VaultStore? _store;

    public LoginViewModel(Func<VaultStore> openVault, Action<VaultSession> onSuccess, TimeProvider time)
    {
        _openVault = openVault; _onSuccess = onSuccess; _time = time;
        try { EnsureStore(); }
        catch (Exception ex) { Error = Describe(ex); }
    }

    private VaultStore EnsureStore()
    {
        if (_store is not null) return _store;
        var store = _openVault();
        VaultFingerprint = VaultStore.Fingerprint(store.Meta.SigningPublicKeyB64);
        return _store = store;
    }

    private static string Describe(Exception ex) => ex switch
    {
        VaultNotFoundException => Strings.VaultNotFound,
        VaultTamperedException => Strings.VaultTampered,
        FormatException => Strings.VaultTampered,
        BadCredentialsException => Strings.BadCredentials,
        AccountLockedException l => string.Format(Strings.AccountLocked, l.Until.ToLocalTime()),
        _ => ErrorText.ForUser(ex)
    };

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoginCommand))] private bool _isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FingerprintText))] private string? _vaultFingerprint;

    public string? FingerprintText => VaultFingerprint is null ? null : $"Mã kho: {VaultFingerprint}";

    private bool CanLogin() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var user = Username;
            var pw = Password;
            Password = "";
            var now = _time.GetUtcNow();
            var session = await Task.Run(() => EnsureStore().Unlock(user, pw, now));
            _onSuccess(session);
        }
        catch (Exception ex) { Error = Describe(ex); }
        finally { IsBusy = false; }
    }
}
