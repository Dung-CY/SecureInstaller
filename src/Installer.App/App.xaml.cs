using System.Windows;
using Installer.App.Views;
using Installer.Core.Errors;
using Installer.Core.Install;

namespace Installer.App;

public partial class App : Application
{
    private AppServices? _services;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, a) =>
        {
            ReportCrash(a.Exception);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            if (a.ExceptionObject is Exception ex) ReportCrash(ex, show: false);
        };
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            ReportCrash(a.Exception, show: false);
            a.SetObserved();
        };
        try { TempWorkspace.SweepStale(TimeSpan.FromDays(1)); } catch { }
        _services = new AppServices();
        var window = new MainWindow { DataContext = _services.Main };
        MainWindow = window;
        window.Show();
    }

    private static void ReportCrash(Exception ex, bool show = true)
    {
        CrashLog.Append(ex);
        if (!show) return;
        try
        {
            MessageBox.Show($"Đã xảy ra lỗi không mong muốn: {ErrorText.ForUser(ex)}", "SecureInstaller",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        try { InstallService.CleanupDocs(); } catch { }
    }
}
