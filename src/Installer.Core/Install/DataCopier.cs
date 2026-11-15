using Installer.Core.Platform;

namespace Installer.Core.Install;

/// <summary>
/// After a package installs, copies the shared, unencrypted folder <c>DuLieu\&lt;package id&gt;</c> (next to the exe)
/// into the folder the software created. Rename rules ("tên gốc =&gt; tên mới", the new name may use {DonVi})
/// apply per file; files without a rule keep their names.
/// </summary>
public static class DataCopier
{
    public const string FolderName = "DuLieu";

    /// <summary>Null when every rule is well formed, otherwise the first problem.</summary>
    public static string? Validate(Model.DataCopy d)
    {
        if (string.IsNullOrWhiteSpace(d.TargetDir)) return "Nhập thư mục nhận dữ liệu";
        foreach (var line in d.Renames ?? new())
            if (Parse(line) is null) return $"Dòng đổi tên không hợp lệ: {line} (dạng: tên gốc => tên mới)";
        return null;
    }

    static (string From, string To)? Parse(string line)
    {
        var i = line.IndexOf("=>", StringComparison.Ordinal);
        if (i < 0) return null;
        var from = Rel(line[..i]); var to = Rel(line[(i + 2)..]);
        return from is null || to is null ? null : (from, to);
    }

    /// <summary>A relative path with forward slashes, or null when empty, rooted or climbing out with "..".</summary>
    static string? Rel(string s)
    {
        var r = s.Trim().Replace('\\', '/');
        if (r.Length == 0 || r.StartsWith('/') || r.Contains(':') || r.Split('/').Any(p => p is "" or "." or "..")) return null;
        return r;
    }

    /// <summary>Copies <paramref name="sourceDir"/> into the expanded target. Returns how many files were copied
    /// (0 when the source folder does not exist); throws on an unsafe target or an I/O failure.</summary>
    public static int Copy(string sourceDir, Model.DataCopy d, string? unit)
    {
        if (!Directory.Exists(sourceDir)) return 0;
        var target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(d.TargetDir));
        var root = Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar;
        var renames = (d.Renames ?? new()).Select(Parse).OfType<(string From, string To)>()
            .ToDictionary(r => r.From, r => r.To.Replace("{DonVi}", unit ?? "", StringComparison.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var src = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDir)) + Path.DirectorySeparatorChar;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var rel = file[src.Length..].Replace('\\', '/');
            var destRel = renames.TryGetValue(rel, out var to) ? to : rel;
            if (Rel(destRel) is null) throw new InvalidDataException("Tên mới không hợp lệ: " + destRel);
            var dest = Path.GetFullPath(Path.Combine(target, destRel));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Tên mới không hợp lệ: " + destRel);
            SafePath.EnsureNoReparseAncestors(dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            count++;
        }
        return count;
    }
}
