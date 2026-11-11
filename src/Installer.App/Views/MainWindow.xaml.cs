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
        if (DataContext is MainViewModel { IsBusyInstalling: true })
        {
            e.Cancel = true;
            MessageBox.Show("Đang cài đặt. Vui lòng đợi hoặc bấm Hủy trước khi thoát.", "SecureInstaller",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
