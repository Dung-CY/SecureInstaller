using System.Text.Json;
using System.Text.Json.Serialization;
using Installer.Core.Model;

namespace Installer.Core.Vault;

/// <summary>What the admin filled in on the package editor form; files are absolute paths picked on this machine.</summary>
public record PackageDraft(string Id, string Name, string Version, InstallerType Type, string InstallerPath,
    string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> ExtraFiles,
    List<string> DocFiles, string? IconPath, string? PreScriptPath, string? PostScriptPath, int ScriptTimeoutSec,
    List<RuleSpec> Rules, UninstallSpec? Uninstall, List<PostAction>? PostActions = null, string? KeepIconPngB64 = null);

/// <summary>Turns a <see cref="PackageDraft"/> into the source-folder layout <see cref="PackageBuilder"/> reads.</summary>
public static class PackageSource
{
    sealed record ManifestOut(string Id, string Name, string Version, InstallerType InstallerType, string InstallerFile,
        string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> Docs,
        List<PostAction> PostActions, List<RuleSpec> Rules, UninstallSpec? Uninstall,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreScript,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PostScript,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ScriptTimeoutSec);

    /// <summary>Writes installer\, docs\, scripts\, icon.png and manifest.json into <paramref name="tempDir"/> and returns it.</summary>
    public static string Create(PackageDraft d, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        // A Script-type package's installer is the script itself and lives in scripts\.
        var isScript = d.Type == InstallerType.Script;
        var installerFile = isScript
            ? Path.GetFileName(d.InstallerPath)
            : CopyAll(new[] { d.InstallerPath }.Concat(d.ExtraFiles), Path.Combine(tempDir, "installer"))[0];
        var docs = CopyAll(d.DocFiles, Path.Combine(tempDir, "docs"));
        var scripts = CopyAll(new[] { isScript ? d.InstallerPath : null, d.PreScriptPath, d.PostScriptPath }
            .Concat(isScript ? d.ExtraFiles : new()).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase), Path.Combine(tempDir, "scripts"));
        if (d.IconPath is { } icon) File.Copy(icon, Path.Combine(tempDir, "icon.png"));
        else if (d.KeepIconPngB64 is { } kept) File.WriteAllBytes(Path.Combine(tempDir, "icon.png"), Convert.FromBase64String(kept));
        var postActions = d.PostActions ?? new();
        // Post-action sources resolve under extras\ at install time, so extra files are mirrored there when any exist.
        if (postActions.Count > 0) CopyAll(d.ExtraFiles, Path.Combine(tempDir, "extras"));

        var pre = d.PreScriptPath is null ? null : Path.GetFileName(d.PreScriptPath);
        var post = d.PostScriptPath is null ? null : Path.GetFileName(d.PostScriptPath);
        var manifest = new ManifestOut(d.Id, d.Name, d.Version, d.Type, installerFile, d.SilentArgs, d.TargetDir,
            d.TimeoutSec, d.Detect, docs, postActions, d.Rules, d.Uninstall, pre, post,
            scripts.Count > 0 ? d.ScriptTimeoutSec : null);
        File.WriteAllBytes(Path.Combine(tempDir, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Json.Options));
        return tempDir;
    }

    /// <summary>Copies files flat into <paramref name="dir"/>; two files with the same name would silently collide, so they are refused.</summary>
    static List<string> CopyAll(IEnumerable<string> files, string dir)
    {
        var names = new List<string>();
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Có hai file cùng tên: {name}");
            Directory.CreateDirectory(dir);
            File.Copy(f, Path.Combine(dir, name));
            names.Add(name);
        }
        return names;
    }
}
