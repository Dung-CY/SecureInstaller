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
}
