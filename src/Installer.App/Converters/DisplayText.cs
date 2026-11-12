using Installer.Core.Licensing;
using Installer.Core.Scanning;
using Installer.Core.Versioning;

namespace Installer.App.Converters;

public static class DisplayText
{
    public const string DateFormat = "dd/MM/yyyy HH:mm";

    public static string Of(LicenseVerdict v) => v switch
    {
        LicenseVerdict.Genuine => "Chính hãng",
        LicenseVerdict.Suspicious => "Nghi vấn",
        LicenseVerdict.CrackSuspected => "Có dấu hiệu crack",
        LicenseVerdict.Unknown => "Không xác định",
        LicenseVerdict.NotInstalled => "Không cài",
        _ => "Không xác định"
    };

    public static string Of(VersionStatus s) => s switch
    {
        VersionStatus.NotInstalled => "Chưa cài",
        VersionStatus.Older => "Cần cập nhật",
        VersionStatus.Equal => "Đã cài",
        VersionStatus.Newer => "Máy có bản mới hơn",
        VersionStatus.Incomparable => "Đã cài (không rõ phiên bản)",
        _ => "Đã cài (không rõ phiên bản)"
    };

    public static string Of(NetworkKind k) => k switch
    {
        NetworkKind.Wireless => "Wi-Fi",
        NetworkKind.Wired => "Có dây",
        NetworkKind.Vpn => "VPN",
        _ => "Khác"
    };

    public static string Of(DateTime? d) => d?.ToString(DateFormat) ?? "—";
}
