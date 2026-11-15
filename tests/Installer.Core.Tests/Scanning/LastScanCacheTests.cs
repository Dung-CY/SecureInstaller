using System.Security.Cryptography;
using Installer.Core.Licensing;
using Installer.Core.Scanning;

namespace Installer.Core.Tests.Scanning;

public class LastScanCacheTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "SI_cache Dự án " + Guid.NewGuid().ToString("N"));
    readonly Guid _vault = Guid.NewGuid();
    readonly byte[] _vk = RandomNumberGenerator.GetBytes(32);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    static LicenseInfo Lic => new("Windows", "Pro", LicenseChannel.Retail, true, null, null, LicenseVerdict.Genuine, new());
    static ScanReport Report(string host, string? serial) => new(DateTimeOffset.Now,
        new HardwareInfo(host, "Dell", "Opti", serial, new(), new(), new(), new()), new NetworkHistory(new(), true), Lic, Lic,
        new KeywordScanResult(new(), false, false), Unit: "LamThao");

    [Fact]
    public void Same_machine_and_vault_reuses_the_scan()
    {
        new LastScanCache(_vault, _vk, _dir).Save(Report("PC-01", "SN1"));
        var r = new LastScanCache(_vault, _vk, _dir).Load("sn1 ", "pc-01");
        Assert.Equal(("PC-01", "LamThao"), (r!.Hardware.Hostname, r.Unit));
    }

    [Fact]
    public void Other_machine_other_vault_or_damage_means_a_new_scan()
    {
        var c = new LastScanCache(_vault, _vk, _dir);
        c.Save(Report("PC-01", "SN1"));
        Assert.Null(c.Load("SN2", "PC-01"));
        Assert.Null(c.Load("SN1", "PC-02"));
        Assert.Null(new LastScanCache(_vault, RandomNumberGenerator.GetBytes(32), _dir).Load("SN1", "PC-01"));
        Assert.Null(new LastScanCache(Guid.NewGuid(), _vk, _dir).Load("SN1", "PC-01"));
        var f = Directory.GetFiles(_dir).Single();
        var bytes = File.ReadAllBytes(f); bytes[^1] ^= 1; File.WriteAllBytes(f, bytes);
        Assert.Null(c.Load("SN1", "PC-01"));
    }
}
