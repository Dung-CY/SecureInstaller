using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Vault;

public class VaultFingerprintTests
{
    [Fact]
    public void Fingerprint_is_sha256_prefix_grouped_4_4_4_4()
    {
        var spki = Enumerable.Range(0, 91).Select(i => (byte)i).ToArray();
        var fp = VaultStore.Fingerprint(Convert.ToBase64String(spki));
        Assert.Matches(new Regex("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$"), fp);
        var hex = Convert.ToHexString(SHA256.HashData(spki)[..8]);
        Assert.Equal($"{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}", fp);
    }

    [Fact]
    public void Fingerprint_differs_for_different_keys()
    {
        var a = VaultStore.Fingerprint(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        var b = VaultStore.Fingerprint(Convert.ToBase64String(new byte[] { 1, 2, 4 }));
        Assert.NotEqual(a, b);
    }
}
