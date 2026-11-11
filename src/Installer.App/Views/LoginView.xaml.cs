using System.Windows;
using System.Windows.Controls;
using Installer.App.ViewModels;

namespace Installer.App.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    private void PwBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.Password = PwBox.Password;
    }
}
