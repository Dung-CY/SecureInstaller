using System.Windows;
using System.Windows.Controls;
using Installer.App.ViewModels;

namespace Installer.App.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is LoginViewModel vm)
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName == nameof(LoginViewModel.Password) && vm.Password.Length == 0 && PwBox.Password.Length != 0)
                        PwBox.Clear();
                };
        };
    }

    private void PwBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.Password = PwBox.Password;
    }
}
