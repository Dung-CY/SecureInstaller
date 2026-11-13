namespace Installer.Core.Install;

public static class CommandLineSplitter
{
    /// <summary>Splits "exe args". Quoted: exe is text up to the next quote. Otherwise: up to and including the first ".exe".</summary>
    public static (string Exe, string Args) Split(string commandLine)
    {
        var s = (commandLine ?? "").Trim();
        if (s.Length == 0) throw new InvalidDataException("Dòng lệnh gỡ cài đặt trống");
        string exe, rest;
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) throw new InvalidDataException("Dòng lệnh gỡ cài đặt không hợp lệ");
            exe = s[1..end];
            rest = s[(end + 1)..];
        }
        else
        {
            int i = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i < 0) throw new InvalidDataException("Dòng lệnh gỡ cài đặt không hợp lệ");
            exe = s[..(i + 4)];
            rest = s[(i + 4)..];
        }
        if (exe.Trim().Length == 0) throw new InvalidDataException("Dòng lệnh gỡ cài đặt không hợp lệ");
        return (exe, rest.Trim());
    }
}
