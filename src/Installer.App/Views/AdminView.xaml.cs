using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Installer.App.ViewModels;

namespace Installer.App.Views;

public partial class AdminView : UserControl
{
    private AdminViewModel? _vm;

    public AdminView()
    {
        InitializeComponent();
        // The view is rebuilt each time the admin VM is shown again (e.g. Back from a history report),
        // so the handler is moved, not added, to avoid old views staying subscribed.
        DataContextChanged += (_, e) => Attach(e.NewValue as AdminViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as AdminViewModel);
    }

    private void Attach(AdminViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm)) return;
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs a)
    {
        if (a.PropertyName == nameof(AdminViewModel.NewPassword) && _vm is { NewPassword.Length: 0 } && PwBox.Password.Length != 0)
            PwBox.Clear();
    }

    private void PwBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdminViewModel vm) vm.NewPassword = PwBox.Password;
    }
}
