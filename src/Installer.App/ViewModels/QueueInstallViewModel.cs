using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Installer.Core.Errors;
using Installer.Core.Install;
using Installer.Core.Model;

namespace Installer.App.ViewModels;

/// <summary>One package in an install/uninstall queue. A class rather than a record: records cannot derive from ObservableObject.</summary>
public partial class QueueItem : ObservableObject
{
    public const string Waiting = "Đang chờ";
    public const string Succeeded = "Thành công";
    public const string Reboot = "Cần khởi động lại";
    public const string Failed = "Lỗi";
    public const string Cancelled = "Đã hủy";
    public const string Skipped = "Đã bỏ qua";

    public QueueItem(PackageEntry package)
    {
        Package = package;
        Icon = PackageRowViewModel.DecodeIcon(package.IconPngB64);
    }

    public PackageEntry Package { get; }
    public string Name => Package.Name;
    public string Version => Package.Version;
    public byte[]? Icon { get; }

    [ObservableProperty] private string _status = Waiting;
    [ObservableProperty] private string? _message;
    /// <summary>Drives the row colour in the view: "ok", "warn", "error", "muted", "active" or "waiting".</summary>
    [ObservableProperty] private string _tone = "waiting";
}

/// <summary>Runs packages one after another (install or uninstall); a failed item never stops the queue.</summary>
public partial class QueueInstallViewModel : ObservableObject
{
    public const string RebootLine = "Hãy khởi động lại máy để hoàn tất.";
    public const string UninstallRunningText = "Đang gỡ";
    public const string CancellingText = "Đang hủy…";
    public const string TimeoutText = "Đã hết thời gian chờ";

    private readonly CancellationTokenSource _cts = new();
    private readonly Action _onBack;

    public QueueInstallViewModel(string title, IReadOnlyList<PackageEntry> items,
        Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run, Action onBack,
        string runningText = "Đang cài")
        : this(title, items, run, onBack, a => new Progress<InstallProgress>(a), runningText) { }

    /// <summary>Tests pass a synchronous progress factory so they don't depend on a SynchronizationContext.</summary>
    internal QueueInstallViewModel(string title, IReadOnlyList<PackageEntry> items,
        Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run, Action onBack,
        Func<Action<InstallProgress>, IProgress<InstallProgress>> progressFactory, string runningText = "Đang cài")
    {
        Title = title;
        RunningText = runningText;
        _onBack = onBack;
        foreach (var p in items) Items.Add(new QueueItem(p));
        Header = $"Phần mềm {Math.Min(1, Items.Count)}/{Items.Count}";
        Step = "Đang chuẩn bị";
        Completion = RunAsync(run, progressFactory);
    }

    public string Title { get; }
    public string RunningText { get; }
    public bool IsUninstall => RunningText == UninstallRunningText;
    public ObservableCollection<QueueItem> Items { get; } = new();

    /// <summary>Completes when every item has finished or been skipped (never faults).</summary>
    public Task Completion { get; }

    [ObservableProperty] private string _header = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIndeterminate))] private int? _percent;
    [ObservableProperty] private string _step = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private QueueItem? _currentItem;
    [ObservableProperty] private bool _hasProblems;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CancelCommand)), NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private bool _isDone;

    public bool IsIndeterminate => Percent is null;

    private async Task RunAsync(Func<PackageEntry, IProgress<InstallProgress>, CancellationToken, Task<InstallOutcome>> run,
        Func<Action<InstallProgress>, IProgress<InstallProgress>> progressFactory)
    {
        var anyReboot = false;
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (_cts.IsCancellationRequested)
            {
                Set(item, QueueItem.Skipped, "muted");
                continue;
            }

            CurrentItem = item;
            Header = $"Phần mềm {i + 1}/{Items.Count}";
            Percent = null;
            Step = "Đang chuẩn bị";
            Set(item, RunningText, "active");

            InstallOutcome outcome;
            var active = true;
            try
            {
                // After Cancel the step keeps showing "Đang hủy…" until the item actually stops.
                var progress = progressFactory(p =>
                {
                    if (active && !IsDone && !_cts.IsCancellationRequested) { Percent = p.Percent; Step = p.Step; }
                });
                outcome = await run(item.Package, progress, _cts.Token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                outcome = new(InstallStatus.Failed, null, ErrorText.Cancelled);
            }
            catch (OperationCanceledException)
            {
                // Cancelled without the user pressing Hủy (e.g. an internal timeout): a failure, not a cancel.
                outcome = new(InstallStatus.Failed, null, TimeoutText);
            }
            catch (Exception ex) { outcome = new(InstallStatus.Failed, null, ErrorText.ForUser(ex)); }
            active = false;

            switch (outcome.Status)
            {
                case InstallStatus.Success:
                    Set(item, QueueItem.Succeeded, "ok");
                    break;
                case InstallStatus.RebootRequired:
                    anyReboot = true;
                    Set(item, QueueItem.Reboot, "warn");
                    break;
                default:
                    // An item that did not finish while Cancel was pressed was stopped by it.
                    if (_cts.IsCancellationRequested) Set(item, QueueItem.Cancelled, "muted");
                    else { Set(item, QueueItem.Failed, "error"); item.Message = outcome.Message; }
                    break;
            }
        }

        CurrentItem = null;
        var ok = Items.Count(x => x.Status is QueueItem.Succeeded or QueueItem.Reboot);
        HasProblems = ok < Items.Count;
        Percent = 100; // never leave an indeterminate bar spinning on the result screen
        Step = _cts.IsCancellationRequested ? "Đã dừng" : "Hoàn tất";
        Result = $"{ok}/{Items.Count} phần mềm thành công." + (anyReboot ? "\n" + RebootLine : "");
        IsDone = true;
        _cts.Dispose(); // IsCancellationRequested stays readable after Dispose; Cancel is a no-op once done
    }

    private static void Set(QueueItem item, string status, string tone)
    {
        item.Status = status;
        item.Tone = tone;
    }

    private bool CanCancel() => !IsDone && !_cts.IsCancellationRequested;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (IsDone || _cts.IsCancellationRequested) return;
        _cts.Cancel();
        Step = CancellingText;
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanBack() => IsDone;

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => _onBack();
}
