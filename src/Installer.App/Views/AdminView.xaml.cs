using System.Windows;
using System.Windows.Controls;
using Installer.App.ViewModels;

namespace Installer.App.Views;

public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is AdminViewModel vm)
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName == nameof(AdminViewModel.NewPassword) && vm.NewPassword.Length == 0 && PwBox.Password.Length != 0)
                        PwBox.Clear();
                };
        };
    }

    private void PwBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdminViewModel vm) vm.NewPassword = PwBox.Password;
    }
}
