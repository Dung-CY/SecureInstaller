using System.Windows;
using Installer.App.Views;
using Installer.Core.Install;

namespace Installer.App;

public partial class App : Application
{
    private AppServices? _services;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, a) =>
        {
            MessageBox.Show($"Đã xảy ra lỗi không mong muốn: {a.Exception.Message}", "SecureInstaller",
                MessageBoxButton.OK, MessageBoxImage.Error);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            System.Diagnostics.Debug.WriteLine("UnhandledException: " + a.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            System.Diagnostics.Debug.WriteLine("UnobservedTaskException: " + a.Exception);
            a.SetObserved();
        };
        try { TempWorkspace.SweepStale(TimeSpan.FromDays(1)); } catch { }
        _services = new AppServices();
        var window = new MainWindow { DataContext = _services.Main };
        MainWindow = window;
        window.Show();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        try { InstallService.CleanupDocs(); } catch { }
    }
}
