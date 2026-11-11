using System.Runtime.InteropServices;
using System.Text;

namespace Installer.Core.Install;

internal static class NativePaths
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, int dwFlags);

    private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    /// <summary>Expands 8.3 names. Returns the input when the path does not exist or on failure.</summary>
    public static string ToLongPath(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return path;
            var sb = new StringBuilder(1024);
            uint n = GetLongPathNameW(path, sb, (uint)sb.Capacity);
            if (n == 0) return path;
            if (n > sb.Capacity)
            {
                sb = new StringBuilder((int)n + 1);
                n = GetLongPathNameW(path, sb, (uint)sb.Capacity);
                if (n == 0) return path;
            }
            return sb.ToString();
        }
        catch { return path; }
    }

    public static void DeleteOnReboot(string path)
    {
        try { MoveFileExW(path, null, MOVEFILE_DELAY_UNTIL_REBOOT); } catch { }
    }
}
