using Installer.Core.Errors;
using Installer.Core.Model;
using Installer.Core.Platform;

namespace Installer.Core.Install;

public record PostActionResult(PostAction Action, bool Ok, string? Error);

public static class PostActionRunner
{
    private const string ErrSrc = "Đường dẫn nguồn không hợp lệ";
    private const string ErrDst = "Đường dẫn đích không hợp lệ";
    private const string ErrSys = "Không được xóa thư mục hệ thống";
    private const string ErrType = "Loại thao tác không hỗ trợ";

    public static List<PostActionResult> Run(IEnumerable<PostAction> actions, string extrasRoot)
    {
        var results = new List<PostActionResult>();
        foreach (var a in actions)
        {
            string? err;
            try
            {
                if (string.Equals(a.Type, "copy", StringComparison.OrdinalIgnoreCase)) err = Copy(a, extrasRoot);
                else if (string.Equals(a.Type, "delete", StringComparison.OrdinalIgnoreCase)) err = Delete(a);
                else err = ErrType;
            }
            catch (UnsafePathException) { err = Strings.UnsafePath; }
            catch (Exception ex) { err = ex.Message; }
            results.Add(new PostActionResult(a, err == null, err));
        }
        return results;
    }

    private static string? Copy(PostAction a, string extrasRoot)
    {
        if (string.IsNullOrWhiteSpace(a.Src) || Path.IsPathRooted(a.Src)) return ErrSrc;
        var rootFull = Path.GetFullPath(extrasRoot);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar)) rootFull += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(extrasRoot, a.Src));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return ErrSrc;

        var dst = ExpandDst(a.Dst);
        if (dst == null) return ErrDst;
        var parent = Path.GetDirectoryName(Path.GetFullPath(dst));
        if (parent == null) return ErrDst;
        SafePath.EnsureNoReparseAncestors(Path.GetFullPath(dst));
        Directory.CreateDirectory(parent);
        File.Copy(full, dst, true);
        return null;
    }

    private static string? Delete(PostAction a)
    {
        var dst = ExpandDst(a.Dst);
        if (dst == null) return ErrDst;
        var norm = Normalize(dst);
        if (IsProtectedPath(norm)) return ErrSys;
        SafePath.EnsureNoReparseAncestors(Path.GetDirectoryName(norm)!);
        if (Directory.Exists(norm)) Directory.Delete(norm, true);
        else if (File.Exists(norm)) File.Delete(norm);
        return null;
    }

    private static string? ExpandDst(string? dst)
    {
        if (string.IsNullOrWhiteSpace(dst)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(dst);
        // Reject alternate data streams / extra colons (only the drive colon at index 1 is allowed).
        if (expanded.Length > 2 && expanded.IndexOf(':', 2) >= 0) return null;
        // Reject device/extended (\\?\, \\.\) and UNC prefixes.
        if (expanded.StartsWith("//") || expanded.StartsWith(new string(Path.DirectorySeparatorChar, 2))
            || expanded.StartsWith("/\\") || expanded.StartsWith("\\/")) return null;
        return Path.IsPathFullyQualified(expanded) ? expanded : null;
    }

    private static string Normalize(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>True when the (expanded, fully qualified) path is a drive root or a protected system folder.</summary>
    internal static bool IsProtectedPath(string path)
    {
        var norm = Normalize(NativePaths.ToLongPath(Normalize(path)));
        var root = Path.GetPathRoot(norm);
        if (norm.Length == 0 || (root != null && norm.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
            return true;
        var sysRoot = Environment.ExpandEnvironmentVariables("%SystemRoot%");
        var candidates = new[]
        {
            sysRoot, Path.Combine(sysRoot, "System32"),
            Environment.ExpandEnvironmentVariables("%ProgramFiles%"),
            Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%"),
            Environment.ExpandEnvironmentVariables("%ProgramData%"),
            Environment.ExpandEnvironmentVariables("%UserProfile%"),
        };
        foreach (var c in candidates)
        {
            if (c.Contains('%') || !Path.IsPathFullyQualified(c)) continue;
            if (norm.Equals(Normalize(NativePaths.ToLongPath(Normalize(c))),StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
