using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Installer.App.Views;

public partial class ScanReportView : UserControl
{
    public ScanReportView() => InitializeComponent();

    // Opens Explorer with the matched file selected; falls back to its folder if the file was moved since the scan.
    void OpenKeywordFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path) return;
        var dir = Path.GetDirectoryName(path);
        string? args = File.Exists(path) ? $"/select,\"{path}\"" : Directory.Exists(dir) ? $"\"{dir}\"" : null;
        if (args is null) { MessageBox.Show("Không còn tìm thấy file hoặc thư mục này.", "SecureInstaller"); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = false }); }
        catch (Exception ex) { MessageBox.Show("Không mở được thư mục: " + ex.Message, "SecureInstaller"); }
    }
}
