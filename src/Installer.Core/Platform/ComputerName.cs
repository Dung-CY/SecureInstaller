using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Installer.Core.Platform;

/// <summary>Computer name rules, the unit-name format, and renaming (Windows 10 and 11: SetComputerNameEx).</summary>
public static class ComputerName
{
    /// <summary>Longest name whose NetBIOS form is not cut; longer names are allowed but warned about.</summary>
    public const int NetBiosLength = 15;
    public const int MaxLength = 63;

    /// <summary>Upper-cased name with Vietnamese marks removed ("đ" → "D"); spaces become "-".</summary>
    public static string Normalize(string input) =>
        string.Join("-", StripMarks(input).ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Null when the name is valid, otherwise the reason in Vietnamese.</summary>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Nhập tên máy mới";
        if (name.Length > MaxLength) return $"Tên máy tối đa {MaxLength} ký tự";
        if (name.Any(c => !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-')))
            return "Tên máy chỉ gồm chữ không dấu, số và dấu gạch ngang (-)";
        if (name[0] == '-' || name[^1] == '-') return "Tên máy không được bắt đầu hay kết thúc bằng dấu gạch ngang";
        if (name.All(char.IsDigit)) return "Tên máy không được chỉ gồm chữ số";
        return null;
    }

    /// <summary>"Lâm thao" → "LamThao": no marks, no spaces, first letter of each word upper-case.</summary>
    public static string NormalizeUnit(string input)
    {
        var words = new string(StripMarks(input).Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // An all-caps word becomes "Hoa"; a word already typed in mixed case ("HoaBinh") keeps its inner capitals.
        return string.Concat(words.Select(w => char.ToUpperInvariant(w[0]) + (w.All(c => !char.IsLower(c)) ? w[1..].ToLowerInvariant() : w[1..])));
    }

    static string StripMarks(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>The name Windows will use after the next restart when a rename is pending, else null.</summary>
    public static string? PendingName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
            var next = k?.GetValue("NV Hostname") as string;
            return !string.IsNullOrEmpty(next) && !next.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ? next : null;
        }
        catch (Exception) { return null; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetComputerNameExW(int nameType, string lpBuffer);
    const int ComputerNamePhysicalDnsHostname = 5;

    /// <summary>Sets the DNS host name (and the derived NetBIOS name); takes effect after a restart. Needs admin rights.</summary>
    public static void Rename(string name)
    {
        if (Validate(name) is { } err) throw new ArgumentException(err, nameof(name));
        if (!SetComputerNameExW(ComputerNamePhysicalDnsHostname, name)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
