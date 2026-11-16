using Installer.Core.Platform;

namespace Installer.Core.Tests.Platform;

public class SecureBootGuideTests
{
    [Theory]
    [InlineData("Dell Inc.", "F2")]
    [InlineData("HP", "F10")]
    [InlineData("LENOVO", "F1")]
    [InlineData("ASUSTeK COMPUTER INC.", "F2")]
    [InlineData("Micro-Star International Co., Ltd.", "Del")]
    [InlineData("Acer", "F2")]
    public void Guide_names_the_bios_key_for_known_makers(string maker, string key) =>
        Assert.Contains(key, SecureBootGuide.For(maker));

    [Theory]
    [InlineData(null)]
    [InlineData("Some OEM")]
    public void Unknown_maker_gets_a_generic_guide(string? maker)
    {
        var g = SecureBootGuide.For(maker);
        Assert.Contains("Secure Boot", g);
        Assert.Contains("BIOS", g);
    }
}
