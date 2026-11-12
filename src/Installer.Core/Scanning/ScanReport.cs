using Installer.Core.Licensing;

namespace Installer.Core.Scanning;

public record ScanReport(DateTimeOffset ScannedAt, HardwareInfo Hardware, NetworkHistory Network,
    LicenseInfo Windows, LicenseInfo Office, KeywordScanResult Keywords, List<UsbStorageRecord>? UsbHistory = null,
    InternetEvidence? Internet = null);
