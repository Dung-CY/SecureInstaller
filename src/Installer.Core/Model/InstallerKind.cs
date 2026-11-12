namespace Installer.Core.Model;

/// <summary>What technology built an installer, which decides its silent-install switches.</summary>
public enum InstallerKind { Unknown, Msi, Inno, Nsis, Burn, InstallShield, Zip, Script }
