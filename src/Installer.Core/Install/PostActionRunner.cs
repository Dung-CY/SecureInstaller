using Installer.Core.Model;

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
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dst))!);
        File.Copy(full, dst, true);
        return null;
    }

    private static string? Delete(PostAction a)
    {
        var dst = ExpandDst(a.Dst);
        if (dst == null) return ErrDst;
        var norm = Normalize(dst);
        if (IsProtected(norm)) return ErrSys;
        if (Directory.Exists(norm)) Directory.Delete(norm, true);
        else if (File.Exists(norm)) File.Delete(norm);
        return null;
    }

    private static string? ExpandDst(string? dst)
    {
        if (string.IsNullOrWhiteSpace(dst)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(dst);
        return Path.IsPathFullyQualified(expanded) ? expanded : null;
    }

    private static string Normalize(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsProtected(string norm)
    {
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
            if (norm.Equals(Normalize(c), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
