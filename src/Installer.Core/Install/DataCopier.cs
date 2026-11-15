using Installer.Core.Platform;

namespace Installer.Core.Install;

/// <summary>Files copied, and the listed files that were not in the data folder (names after {DonVi} is filled in).</summary>
public sealed record DataCopyResult(int Copied, List<string> Missing);

/// <summary>
/// After a package installs, copies the files listed in its <see cref="Model.DataCopy"/> from the shared, unencrypted
/// folder <c>DuLieu\&lt;package id&gt;</c> (next to the exe) into the folder the software created. Only listed files
/// are copied, so data kept for other units never reaches this machine. Line forms:
/// <c>{DonVi}.txt =&gt; config.txt</c> (pick the unit's file, copy it under a common name) or <c>huongdan.pdf</c> (same name).
/// </summary>
public static class DataCopier
{
    public const string FolderName = "DuLieu";

    /// <summary>Null when the settings are usable, otherwise the first problem.</summary>
    public static string? Validate(Model.DataCopy d)
    {
        if (string.IsNullOrWhiteSpace(d.TargetDir)) return "Nhập thư mục nhận dữ liệu";
        var lines = (d.Files ?? new()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return "Nhập ít nhất một file cần chép";
        foreach (var line in lines)
            if (Parse(line) is null) return $"Dòng không hợp lệ: {line} (dạng: tên trong DuLieu => tên khi chép)";
        return null;
    }

    static (string From, string To)? Parse(string line)
    {
        var i = line.IndexOf("=>", StringComparison.Ordinal);
        var from = Rel(i < 0 ? line : line[..i]);
        var to = i < 0 ? from : Rel(line[(i + 2)..]);
        return from is null || to is null ? null : (from, to);
    }

    /// <summary>A relative path with forward slashes, or null when empty, rooted or climbing out with "..".</summary>
    static string? Rel(string s)
    {
        var r = s.Trim().Replace('\\', '/');
        if (r.Length == 0 || r.StartsWith('/') || r.Contains(':') || r.Split('/').Any(p => p is "" or "." or "..")) return null;
        return r;
    }

    /// <summary>Copies each listed file into the expanded target; throws on an invalid line, an unsafe target or an I/O failure.</summary>
    public static DataCopyResult Copy(string sourceDir, Model.DataCopy d, string? unit)
    {
        if (Validate(d) is { } err) throw new InvalidDataException(err);
        var target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(d.TargetDir));
        var root = Path.TrimEndingDirectorySeparator(target) + Path.DirectorySeparatorChar;
        var src = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDir)) + Path.DirectorySeparatorChar;
        string Fill(string s) => s.Replace("{DonVi}", unit ?? "", StringComparison.OrdinalIgnoreCase);
        var copied = 0;
        var missing = new List<string>();
        foreach (var line in d.Files!.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var (fromRaw, toRaw) = Parse(line)!.Value;
            string from = Fill(fromRaw), to = Fill(toRaw);
            if (Rel(from) is null || Rel(to) is null) throw new InvalidDataException("Tên file không hợp lệ: " + line);
            var file = Path.GetFullPath(Path.Combine(src, from));
            var dest = Path.GetFullPath(Path.Combine(target, to));
            if (!file.StartsWith(src, StringComparison.OrdinalIgnoreCase) || !dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Tên file không hợp lệ: " + line);
            if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) { missing.Add(from); continue; }
            SafePath.EnsureNoReparseAncestors(dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            copied++;
        }
        return new DataCopyResult(copied, missing);
    }
}
