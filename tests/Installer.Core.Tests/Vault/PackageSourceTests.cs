using System.Text.Json;
using Installer.Core.Model;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Vault;

public class PackageSourceTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "SI_pkgsrc_" + Guid.NewGuid().ToString("N"), "Dự án 1");

    public PackageSourceTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(Path.GetDirectoryName(_tmp)!, true); } catch { } }

    string Touch(string rel, string text = "x")
    {
        var p = Path.Combine(_tmp, "picked", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    PackageDraft Draft(List<string> extras, string? preScript = null) => new(
        "7zip", "7-Zip", "24.08", InstallerType.Msi, Touch("Bản cài/7z.msi"), "ALLUSERS=1", null, 900,
        new DetectRule("{23170F69-40C1-2702-2408-000001000000}", null), extras, new() { Touch("Hướng dẫn.pdf") },
        null, preScript, null, 120, new() { new RuleSpec("block_if_windows_cracked") }, new UninstallSpec("msi"));

    [Fact]
    public void Script_type_puts_script_and_extra_files_in_scripts_folder()
    {
        var d = Draft(new() { Touch("data/cfg.ini", "k=v") }) with { Type = InstallerType.Script, InstallerPath = Touch("run.cmd", "@exit /b 0"), Uninstall = null };
        var folder = PackageSource.Create(d, Path.Combine(_tmp, "out2"));
        Assert.True(File.Exists(Path.Combine(folder, "scripts", "run.cmd")));
        Assert.Equal("k=v", File.ReadAllText(Path.Combine(folder, "scripts", "cfg.ini")));
        Assert.False(Directory.Exists(Path.Combine(folder, "installer")));
        Assert.Equal("run.cmd", JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json"))).RootElement.GetProperty("installer_file").GetString());
    }

    [Fact]
    public void Writes_a_manifest_PackageBuilder_accepts_with_extras_beside_the_installer()
    {
        var draft = Draft(new() { Touch("cấu hình/config.ini", "k=v") }, preScript: Touch("pre.ps1"));
        var folder = PackageSource.Create(draft, Path.Combine(_tmp, "out"));

        Assert.True(File.Exists(Path.Combine(folder, "installer", "7z.msi")));
        Assert.Equal("k=v", File.ReadAllText(Path.Combine(folder, "installer", "config.ini")));
        Assert.True(File.Exists(Path.Combine(folder, "scripts", "pre.ps1")));
        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json"))).RootElement;
        Assert.Equal("pre.ps1", json.GetProperty("pre_script").GetString());
        Assert.Equal(120, json.GetProperty("script_timeout_sec").GetInt32());
        Assert.False(json.TryGetProperty("post_script", out _));

        var entry = PackageBuilder.Build(folder, Path.Combine(_tmp, "vault-root"));
        Assert.Equal(("7zip", "7-Zip", "24.08", InstallerType.Msi, "7z.msi"),
            (entry.Id, entry.Name, entry.Version, entry.InstallerType, entry.InstallerFile));
        Assert.Equal("ALLUSERS=1", entry.SilentArgs);
        Assert.Equal(900, entry.TimeoutSec);
        Assert.Equal("{23170F69-40C1-2702-2408-000001000000}", entry.Detect.ProductCode);
        Assert.Equal(new[] { "Hướng dẫn.pdf" }, entry.Docs);
        Assert.Equal("block_if_windows_cracked", Assert.Single(entry.Rules).Type);
        Assert.Equal("msi", entry.Uninstall!.Type);
    }

    [Fact]
    public void Script_fields_are_omitted_when_no_script_is_set()
    {
        var folder = PackageSource.Create(Draft(new()), Path.Combine(_tmp, "out"));
        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json"))).RootElement;
        Assert.False(json.TryGetProperty("pre_script", out _));
        Assert.False(json.TryGetProperty("script_timeout_sec", out _));
    }

    [Fact]
    public void Duplicate_extra_file_names_are_rejected()
    {
        var draft = Draft(new() { Touch("a/config.ini"), Touch("b/CONFIG.ini") });
        Assert.Throws<InvalidDataException>(() => PackageSource.Create(draft, Path.Combine(_tmp, "out")));
    }

    [Fact]
    public void An_extra_file_named_like_the_installer_is_rejected()
    {
        var draft = Draft(new() { Touch("khác/7z.msi") });
        Assert.Throws<InvalidDataException>(() => PackageSource.Create(draft, Path.Combine(_tmp, "out")));
    }

    [Fact]
    public void Edit_round_trip_keeps_post_actions_and_the_current_icon()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var post = new List<PostAction> { new("copy", "config.ini", @"C:\ProgramDatazip\config.ini") };
        var draft = Draft(new() { Touch("config.ini", "k=v") }) with
            { PostActions = post, KeepIconPngB64 = Convert.ToBase64String(png) };
        var folder = PackageSource.Create(draft, Path.Combine(_tmp, "out"));

        var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json"))).RootElement;
        Assert.Equal("config.ini", json.GetProperty("post_actions")[0].GetProperty("src").GetString());
        Assert.True(File.Exists(Path.Combine(folder, "extras", "config.ini"))); // where post-actions look at install time
        var entry = PackageBuilder.Build(folder, Path.Combine(_tmp, "vault-root"));
        Assert.Equal(post, entry.PostActions);
        Assert.Equal(Convert.ToBase64String(png), entry.IconPngB64);
    }
}
