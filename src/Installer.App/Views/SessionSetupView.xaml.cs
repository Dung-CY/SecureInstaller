using System.Windows.Controls;

namespace Installer.App.Views;

public partial class SessionSetupView : UserControl
{
    public SessionSetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => UnitBox.Focus();
    }
}
