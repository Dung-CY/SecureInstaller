using Installer.Core.Platform;
using Microsoft.Win32;

namespace Installer.Core.Tests.Platform;

public class RegistryReaderTests
{
    [Fact]
    public void RegistryReader_missing_key_returns_empty_and_null()
    {
        var path = @"Software\SecureInstallerTests_" + Guid.NewGuid().ToString("N");
        var r = new RegistryReader();
        Assert.Empty(r.GetSubKeyNames(RegistryHive.CurrentUser, RegistryView.Registry64, path));
        Assert.Null(r.GetValue(RegistryHive.CurrentUser, RegistryView.Registry64, path, "X"));
    }
}
