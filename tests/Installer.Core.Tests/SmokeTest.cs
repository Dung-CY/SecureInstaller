namespace Installer.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void Core_assembly_loads()
    {
        Assert.NotNull(typeof(Installer.Core.Strings).Assembly);
    }
}
