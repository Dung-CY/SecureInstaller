using System.Globalization;
using System.Text;

namespace Installer.Core.Logging;

public static class CsvExporter
{
    private const string Header = "Thời gian,Người dùng,Máy,Serial BIOS,Hành động,Phần mềm,Phiên bản,Trạng thái,Mã thoát,Chi tiết";

    public static void Export(IEnumerable<LogEntry> entries, string path)
    {
        var sb = new StringBuilder(Header).Append("\r\n");
        foreach (var e in entries.OrderBy(x => x.Ts))
        {
            sb.Append(string.Join(",", new[]
            {
                S(e.Ts.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                S(e.Username), S(e.Hostname), S(e.BiosSerial), S(e.Action), S(e.PackageId), S(e.PackageVersion),
                S(e.Status), e.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "", S(e.Detail)
            })).Append("\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>One row per scan: Thời gian, Đơn vị, Hãng máy, Số seri máy, MAC card vật lý, Seri ổ cứng (lists separated by ";").</summary>
    public static void ExportScans(IEnumerable<Scanning.ScanHistoryItem> items, string path)
    {
        var sb = new StringBuilder("Thời gian,Đơn vị,Hãng máy,Số seri máy,MAC card vật lý,Seri ổ cứng\r\n");
        foreach (var i in items.OrderBy(x => x.Ts))
        {
            var hw = i.Report.Hardware;
            sb.Append(string.Join(",", new[]
            {
                S(i.Ts.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), S(i.Report.Unit), S(hw.Manufacturer),
                S(hw.BiosSerial), S(string.Join(";", Scanning.ScanHistory.PhysicalMacs(hw))), S(string.Join(";", Scanning.ScanHistory.DiskSerials(hw))),
            })).Append("\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string S(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (v[0] is '=' or '+' or '-' or '@') v = "'" + v;
        if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) v = "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
