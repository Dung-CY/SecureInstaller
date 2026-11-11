using Installer.Core.Errors;

namespace Installer.Core.Platform;

public static class SafePath
{
    /// <summary>
    /// Walks from the drive root down to <paramref name="fullPath"/>; throws <see cref="UnsafePathException"/>
    /// when any existing component is a reparse point (junction/symlink). Missing components are skipped.
    /// Input must be a fully qualified local drive path (no UNC, no \?\ prefix).
    /// </summary>
    public static void EnsureNoReparseAncestors(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath)
            || fullPath.StartsWith(@"\\") || fullPath.StartsWith("//") || fullPath.StartsWith(@"\/") || fullPath.StartsWith(@"/\")
            || fullPath.Length < 3 || fullPath[1] != ':')
            throw new UnsafePathException(Strings.UnsafePath);

        var full = Path.GetFullPath(fullPath);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) throw new UnsafePathException(Strings.UnsafePath);

        var current = root;
        var parts = full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            FileAttributes attrs;
            try { attrs = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (Exception) { throw new UnsafePathException(Strings.UnsafePath); }
            if ((attrs & FileAttributes.ReparsePoint) != 0) throw new UnsafePathException(Strings.UnsafePath);
        }
    }
}
