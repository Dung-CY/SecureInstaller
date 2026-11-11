using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core;
using Installer.Core.Vault;

namespace Installer.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly Func<VaultStore> _openVault;
    private readonly Action<VaultSession> _onSuccess;
    private readonly TimeProvider _time;

    public LoginViewModel(Func<VaultStore> openVault, Action<VaultSession> onSuccess, TimeProvider time)
    {
        _openVault = openVault; _onSuccess = onSuccess; _time = time;
    }

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
        VaultSession? session = null;
        try
        {
            var user = Username;
            var pw = Password;
            var now = _time.GetUtcNow();
            string? fp = null;
            session = await Task.Run(() =>
            {
                var store = _openVault();
                fp = VaultStore.Fingerprint(store.Meta.SigningPublicKeyB64);
                return store.Unlock(user, pw, now);
            });
            VaultFingerprint = fp;
        }
        catch (VaultNotFoundException) { Error = Strings.VaultNotFound; }
        catch (VaultTamperedException) { Error = Strings.VaultTampered; }
        catch (BadCredentialsException) { Error = Strings.BadCredentials; }
        catch (AccountLockedException ex) { Error = string.Format(Strings.AccountLocked, ex.Until); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }

        if (session is not null) _onSuccess(session);
    }
}
