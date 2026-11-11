using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Errors;
using Installer.Core.Install;

namespace Installer.App.ViewModels;

public partial class InstallViewModel : ObservableObject
{
    public const string RebootLine = "Hãy khởi động lại máy để hoàn tất.";

    private readonly CancellationTokenSource _cts = new();
    private readonly Action _onBack;

    public InstallViewModel(string packageTitle,
        Func<IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run, Action onBack)
        : this(packageTitle, run, onBack, a => new Progress<InstallProgress>(a)) { }

    /// <summary>Tests pass a synchronous progress factory so they don't depend on a SynchronizationContext.</summary>
    internal InstallViewModel(string packageTitle,
        Func<IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run, Action onBack,
        Func<Action<InstallProgress>, IProgress<InstallProgress>> progressFactory)
    {
        Title = packageTitle;
        _onBack = onBack;
        Step = "Đang chuẩn bị";
        Completion = RunAsync(run, progressFactory);
    }

    public string Title { get; }

    /// <summary>Completes when the install has finished (never faults).</summary>
    public Task Completion { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIndeterminate))] private int? _percent;
    [ObservableProperty] private string _step = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CancelCommand)), NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private bool _isDone;
    [ObservableProperty] private bool _isSuccess;

    public bool IsIndeterminate => Percent is null;

    private bool _finished;

    private async Task RunAsync(Func<IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run,
        Func<Action<InstallProgress>, IProgress<InstallProgress>> progressFactory)
    {
        InstallOutcome outcome;
        try
        {
            var progress = progressFactory(p => { if (!_finished) { Percent = p.Percent; Step = p.Step; } });
            outcome = await run(progress, _cts.Token);
        }
        catch (OperationCanceledException) { outcome = new(InstallStatus.Failed, null, ErrorText.Cancelled); }
        catch (Exception ex) { outcome = new(InstallStatus.Failed, null, ErrorText.ForUser(ex)); }

        _finished = true;
        Result = outcome.Status == InstallStatus.RebootRequired ? outcome.Message + "\n" + RebootLine : outcome.Message;
        IsSuccess = outcome.Status is InstallStatus.Success or InstallStatus.RebootRequired;
        if (IsSuccess) Percent = 100;
        Step = IsSuccess ? "Hoàn tất" : "Không thành công";
        IsDone = true;
    }

    private bool CanCancel() => !IsDone;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts.Cancel();

    private bool CanBack() => IsDone;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => _onBack();
}
