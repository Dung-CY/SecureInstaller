using System.ComponentModel;
using System.Windows;
using Installer.App.ViewModels;

namespace Installer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel { CloseBlockedMessage: { } msg })
        {
            e.Cancel = true;
            MessageBox.Show(msg, "SecureInstaller",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
