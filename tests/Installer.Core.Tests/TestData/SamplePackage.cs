using System.Text;

namespace Installer.Core.Tests.TestData;

public static class SamplePackage
{
    public static string Create(string parent, string id = "demo", string version = "1.0", bool withDocs = true,
        bool withInstaller = true, string? manifestOverride = null)
    {
        var dir = Path.Combine(parent, "src_" + id + "_" + version + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        var manifest = manifestOverride ?? $$"""
        {
          "id": "{{id}}", "name": "Demo {{id}}", "version": "{{version}}",
          "installer_type": "exe", "installer_file": "setup.exe", "silent_args": "/S"
        }
        """;
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest, new UTF8Encoding(false));
        if (withInstaller)
        {
            Directory.CreateDirectory(Path.Combine(dir, "installer"));
            File.WriteAllBytes(Path.Combine(dir, "installer", "setup.exe"), Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
        }
        if (withDocs)
        {
            Directory.CreateDirectory(Path.Combine(dir, "docs"));
            File.WriteAllText(Path.Combine(dir, "docs", "HuongDan.docx"), "huong dan");
        }
        Directory.CreateDirectory(Path.Combine(dir, "extras"));
        File.WriteAllText(Path.Combine(dir, "extras", "a.ini"), "[a]\nb=1");
        return dir;
    }

    /// <summary>Zip-type package: installer/app.zip holds one file; postActionsJson is a raw JSON array.</summary>
    public static string CreateZip(string parent, string targetDir, string postActionsJson = "[]", string id = "ziptest", string version = "1.0")
    {
        var manifest = $$"""
        {
          "id": "{{id}}", "name": "Zip {{id}}", "version": "{{version}}",
          "installer_type": "zip", "installer_file": "app.zip", "target_dir": {{System.Text.Json.JsonSerializer.Serialize(targetDir)}},
          "post_actions": {{postActionsJson}}
        }
        """;
        var dir = Create(parent, id, version, manifestOverride: manifest, withInstaller: false);
        Directory.CreateDirectory(Path.Combine(dir, "installer"));
        using (var za = System.IO.Compression.ZipFile.Open(Path.Combine(dir, "installer", "app.zip"), System.IO.Compression.ZipArchiveMode.Create))
        {
            var e = za.CreateEntry("hello.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("hi");
        }
        return dir;
    }
}
