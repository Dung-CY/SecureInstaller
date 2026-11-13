using System.Text.Json;
using System.Text.Json.Serialization;
using Installer.Core.Model;

namespace Installer.Core.Vault;

/// <summary>What the admin filled in on the package editor form; files are absolute paths picked on this machine.</summary>
public record PackageDraft(string Id, string Name, string Version, InstallerType Type, string? InstallerPath,
    string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> ExtraFiles,
    List<string> DocFiles, string? IconPath, string? PreScriptPath, string? PostScriptPath, int ScriptTimeoutSec,
    List<RuleSpec> Rules, UninstallSpec? Uninstall, List<PostAction>? PostActions = null, string? KeepIconPngB64 = null,
    InstallerKind Kind = InstallerKind.Unknown, string? UninstallScriptPath = null,
    List<ExtraFile>? ExtraEntries = null, PackageEdit? Edit = null);

/// <summary>An extra file with the relative path (forward slashes) it keeps under installer.</summary>
public record ExtraFile(string Source, string Rel);

/// <summary>Files of the stored package that an edit keeps (names relative to their folder); Extras null means keep all of them.</summary>
public record KeptFiles(string? Installer, List<string>? Extras, List<string> Docs, string? PreScript, string? PostScript, string? UninstallScript);

/// <summary>Marks a draft as an edit of <paramref name="Existing"/>; with no file change only the catalog metadata is rewritten.</summary>
public record PackageEdit(PackageEntry Existing, KeptFiles Kept, bool FilesChanged);

/// <summary>Turns a <see cref="PackageDraft"/> into the source-folder layout <see cref="PackageBuilder"/> reads.</summary>
public static class PackageSource
{
    sealed record ManifestOut(string Id, string Name, string Version, InstallerType InstallerType, string InstallerFile,
        string SilentArgs, string? TargetDir, int TimeoutSec, DetectRule Detect, List<string> Docs,
        List<PostAction> PostActions, List<RuleSpec> Rules, UninstallSpec? Uninstall,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreScript,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PostScript,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ScriptTimeoutSec,
        InstallerKind InstallerKind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UninstallScript);

    /// <summary>Writes installer\, docs\, scripts\, icon.png and manifest.json into <paramref name="tempDir"/> and returns it.</summary>
    public static string Create(PackageDraft d, string tempDir)
    {
        Directory.CreateDirectory(tempDir);
        var instPath = d.InstallerPath ?? throw new InvalidDataException("Chọn file cài");
        var entries = d.ExtraEntries ?? new();
        // A Script-type package's installer is the script itself and lives in scripts\.
        var isScript = d.Type == InstallerType.Script;
        var instDir = Path.Combine(tempDir, "installer");
        var instNames = isScript ? new List<string>() : CopyAll(new[] { instPath }.Concat(d.ExtraFiles), instDir);
        var installerFile = isScript ? Path.GetFileName(instPath) : instNames[0];
        if (!isScript) CopyEntries(entries, instDir, instNames);
        var docs = CopyAll(d.DocFiles, Path.Combine(tempDir, "docs"));
        var scriptsDir = Path.Combine(tempDir, "scripts");
        var scripts = CopyAll(new[] { isScript ? instPath : null, d.PreScriptPath, d.PostScriptPath, d.UninstallScriptPath }
            .Concat(isScript ? d.ExtraFiles : new()).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase), scriptsDir);
        if (isScript) CopyEntries(entries, scriptsDir, scripts);
        if (d.IconPath is { } icon) File.Copy(icon, Path.Combine(tempDir, "icon.png"));
        else if (d.KeepIconPngB64 is { } kept) File.WriteAllBytes(Path.Combine(tempDir, "icon.png"), Convert.FromBase64String(kept));
        var postActions = d.PostActions ?? new();
        // Post-action sources resolve under extras\ at install time, so extra files are mirrored there when any exist.
        if (postActions.Count > 0)
        {
            var mirrored = CopyAll(d.ExtraFiles, Path.Combine(tempDir, "extras"));
            CopyEntries(entries, Path.Combine(tempDir, "extras"), mirrored);
        }

        var pre = d.PreScriptPath is null ? null : Path.GetFileName(d.PreScriptPath);
        var post = d.PostScriptPath is null ? null : Path.GetFileName(d.PostScriptPath);
        var manifest = new ManifestOut(d.Id, d.Name, d.Version, d.Type, installerFile, d.SilentArgs, d.TargetDir,
            d.TimeoutSec, d.Detect, docs, postActions, d.Rules, d.Uninstall, pre, post,
            scripts.Count > 0 ? d.ScriptTimeoutSec : null, d.Kind,
            d.UninstallScriptPath is null ? null : Path.GetFileName(d.UninstallScriptPath));
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

    /// <summary>Copies files keeping their relative paths; rejects rooted or ".." paths, reparse points and duplicates (by relative path).</summary>
    static void CopyEntries(IEnumerable<ExtraFile> entries, string dir, List<string> taken)
    {
        foreach (var (src, relRaw) in entries)
        {
            var rel = relRaw.Replace('\\', '/');
            if (rel.StartsWith('/') || Path.IsPathRooted(rel) || rel.Split('/').Any(s => s is "" or "." or ".."))
                throw new InvalidDataException($"Đường dẫn file đi kèm không hợp lệ: {relRaw}");
            if (taken.Contains(rel, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Có hai file cùng đường dẫn: {rel}");
            if ((File.GetAttributes(src) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Không hỗ trợ liên kết (reparse point): {src}");
            var dest = Path.GetFullPath(Path.Combine(dir, rel));
            if (!dest.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Đường dẫn file đi kèm không hợp lệ: {relRaw}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest);
            taken.Add(rel);
        }
    }

    /// <summary>Lists every file under a folder as <c>&lt;folder name&gt;/sub/file</c>; refuses reparse points anywhere in the tree.</summary>
    public static List<ExtraFile> ReadFolder(string folder)
    {
        var root = new DirectoryInfo(folder);
        var acc = new List<ExtraFile>();
        void Walk(DirectoryInfo dir, string rel)
        {
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Không hỗ trợ liên kết (reparse point): {dir.FullName}");
            foreach (var e in dir.EnumerateFileSystemInfos().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (e is DirectoryInfo sub) Walk(sub, rel + "/" + e.Name);
                else if ((e.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Không hỗ trợ liên kết (reparse point): {e.FullName}");
                else acc.Add(new ExtraFile(e.FullName, rel + "/" + e.Name));
            }
        }
        Walk(root, root.Name);
        return acc;
    }

    /// <summary>Copies the metadata fields of an edit onto the stored entry; file-related fields and keys stay untouched.</summary>
    public static PackageEntry ApplyMeta(PackageEntry e, PackageDraft d) => e with
    {
        Name = d.Name, Version = d.Version, SilentArgs = d.SilentArgs, TargetDir = d.TargetDir, TimeoutSec = d.TimeoutSec,
        Detect = d.Detect, PostActions = d.PostActions ?? new(), Rules = d.Rules, Uninstall = d.Uninstall,
        ScriptTimeoutSec = d.ScriptTimeoutSec > 0 ? d.ScriptTimeoutSec : e.ScriptTimeoutSec, Kind = d.Kind,
        IconPngB64 = d.IconPath is null ? d.KeepIconPngB64 : e.IconPngB64
    };
}
