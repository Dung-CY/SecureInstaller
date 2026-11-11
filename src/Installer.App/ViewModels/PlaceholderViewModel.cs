using Installer.Core.Scanning;

namespace Installer.App.ViewModels;

/// <summary>Temporary landing page after the scan report; replaced by the package list in Task 18.</summary>
public record PlaceholderViewModel(string Message, ScanReport Report);
